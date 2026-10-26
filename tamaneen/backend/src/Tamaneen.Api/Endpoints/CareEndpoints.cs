using System.Text.Json;
using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamaneen.Api.Auth;
using Tamaneen.Api.Clinical;
using Tamaneen.Api.Patients;
using Tamaneen.Clinical.Care;
using Tamaneen.Crisis;
using Tamaneen.Crisis.Rules;
using Tamaneen.Hosting;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Endpoints;

/// <summary>Crisis screen (P06, help button), home (P09), mood (P30), homework (P10, P29), safety plan (P37), public config.</summary>
public static class CareEndpoints
{
    private static readonly Lazy<CrisisScripts> Scripts = new(CrisisRuleCatalog.LoadScripts);

    public sealed record PilotCityDto(string Code, string Name);

    public sealed record PublicConfigDto(string EmergencyNumber, string SupportLine, int MatchWithinHours, int TriageWithinHours, int MinAge, string PilotRegion, IReadOnlyList<PilotCityDto> PilotCities, string TimeZone, bool SignupsOpen, int ChangeTherapistWithinHours = 24);

    public sealed record CrisisStatusDto(bool Locked, bool EventOpen, bool Acknowledged, string? ContactPhoneLast4, string? Script);

    public sealed record HelpResponseDto(bool Crisis, int Level, string? Script, bool EventOpen);

    /// <summary>Optional body of POST /v1/crisis/help: the id the device generated for this press (idempotency key).</summary>
    public sealed record HelpRequestBody(Guid? RequestId);

    public sealed record HomeworkTodayDto(Guid Id, string Title, int? Minutes, int DoneDays, int TotalDays);

    public sealed record FeedbackDto(string TherapistName, string Initials, string Text, DateTimeOffset At);

    public sealed record HomeSummaryDto(string Nickname, SchedulingEndpoints.AppointmentDto? NextAppointment, HomeworkTodayDto? TodayHomework, FeedbackDto? LatestFeedback, int? MoodToday, bool HasSafetyPlan, IntakeEndpoints.TriageStateDto Triage,
        AppointmentNoticeDto? AppointmentNotice);

    /// <summary>A session the therapist cancelled (P67) or marked missed (P63) in the last 7 days, until the patient books again.</summary>
    public sealed record AppointmentNoticeDto(Guid AppointmentId, string Kind);

    public sealed record MoodEntryRequest(int Score, List<string>? Tags, string? Note);

    public sealed record MoodEntryDto(Guid Id, int Score, IReadOnlyList<string> Tags, DateTimeOffset At);

    public sealed record MoodSaveResponse(bool Crisis, int Level, string? Script, MoodEntryDto Entry);

    public sealed record HomeworkItemDto(Guid Id, string Title, string TherapistName, string Frequency, DateOnly EndsOn, bool DueToday, int DoneThisPeriod, bool HasNewFeedback, bool Completed);

    public sealed record HomeworkListDto(IReadOnlyList<HomeworkItemDto> Active, IReadOnlyList<HomeworkItemDto> Completed);

    public sealed record LatestFeedbackDto(string Text, DateTimeOffset At, string Initials);

    public sealed record TodayCheckinDto(string Status, int? Feeling, string? Note);

    public sealed record HomeworkDetailDto(Guid Id, string Title, string TherapistName, string Frequency, DateOnly EndsOn, bool DueToday, int DoneThisPeriod, bool HasNewFeedback, bool Completed,
        IReadOnlyList<string> Instructions, int? Minutes, int DoneDays, int TotalDays, LatestFeedbackDto? LatestFeedback, TodayCheckinDto? TodayCheckin);

    public sealed record CheckinRequest(string? Status, int? Feeling, string? Note);

    public sealed record CheckinResponse(bool Crisis, int Level, string? Script, HomeworkDetailDto Homework);

    /// <summary>A personal help contact in section 4 of the safety plan (P37), shown as a tel: button.</summary>
    public sealed record PlanContactDto(string Name, string Phone);

    public sealed record SafetyPlanDto(IReadOnlyDictionary<string, string> Sections, IReadOnlyList<PlanContactDto> Contacts, DateTimeOffset? UpdatedAt, string? CoAuthor, bool Crisis = false, string? Script = null, int Version = 0);

    public sealed record UpdateSafetyPlanRequest(Dictionary<string, string>? Sections, List<PlanContactDto>? Contacts);

    /// <summary>The encrypted plan. Older plans were stored as the bare section dictionary; both shapes are read.</summary>
    public sealed record StoredSafetyPlan(Dictionary<string, string> Sections, List<PlanContactDto>? Contacts);

    public const int MaxPlanContacts = 5;
    public const int MaxPlanContactName = 100;

    public static IEndpointRouteBuilder MapCareEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/meta/config", PublicConfig).AllowAnonymous();

        var g = app.MapGroup("/v1").RequireAuthorization(AuthSetup.PatientPolicy);
        g.MapGet("/crisis/status", CrisisStatusAsync);
        g.MapPost("/crisis/help", HelpAsync);
        g.MapGet("/home", HomeAsync);
        // The writes the patient app's outbox (P66) queues offline carry an Idempotency-Key: a resend gets the first result.
        g.MapPost("/mood", SaveMoodAsync).WithIdempotency();
        g.MapGet("/mood", MoodAsync);
        g.MapGet("/homework", HomeworkListAsync);
        g.MapGet("/homework/{id:guid}", HomeworkDetailAsync);
        g.MapPost("/homework/{id:guid}/checkins", CheckinAsync).WithIdempotency();
        g.MapGet("/safety-plan", SafetyPlanAsync);
        g.MapPut("/safety-plan", UpdateSafetyPlanAsync).WithIdempotency();
        return app;
    }

    internal static Ok<PublicConfigDto> PublicConfig(HttpContext http, IOptions<PlatformOptions> options)
    {
        var o = options.Value;
        var en = LocalizationSetup.RequestLanguage(http) == "en";
        return TypedResults.Ok(new PublicConfigDto(
            o.EmergencyNumber, o.SupportLine, o.MatchWithinHours, o.TriageWithinHours, o.MinAge,
            en ? o.RegionNameEn : o.RegionNameAr,
            o.Cities.Select(c => new PilotCityDto(c.Code, en ? c.NameEn : c.NameAr)).ToList(),
            o.TimeZone,
            o.SignupsOpen,
            o.ChangeTherapistWithinHours));
    }

    // ---------- P06 ----------

    internal static async Task<Ok<CrisisStatusDto>> CrisisStatusAsync(PatientContext p, IOptions<PlatformOptions> platform, CancellationToken ct)
    {
        var locked = await p.IsChatLockedAsync(ct).ConfigureAwait(false);
        var open = await p.Db.CrisisEvents.AsNoTracking().Where(e => e.PatientId == p.PatientId && e.Status != CrisisEventStatus.Closed).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var profile = await p.ProfileAsync(ct).ConfigureAwait(false);
        var safety = await p.DecryptJsonAsync<SafetyInfoData>(Fields.SafetyInfo, profile.EmergencyContactCipher, ct).ConfigureAwait(false);
        var phone = safety?.ContactPhone;
        if (phone is null && profile.ContactType == "phone")
        {
            phone = await p.DecryptOptionalAsync(Fields.Contact, profile.ContactCipher, ct).ConfigureAwait(false);
        }

        var digits = phone is null ? null : new string(phone.Where(char.IsDigit).ToArray());
        var script = open is null ? null : Scripts.Value.Render(CrisisScripts.KindFor(open.Level), p.Language, platform.Value.EmergencyNumber, platform.Value.SupportLine);
        return TypedResults.Ok(new CrisisStatusDto(locked, open is not null, open is { IsOwned: true }, digits is { Length: >= 4 } ? digits[^4..] : null, script));
    }

    /// <summary>
    /// "Help now" / "Ask a professional to call me now": always a Critical event, paged at once. With a request id
    /// the call is idempotent: a double tap or a queued resend (EC-04) gets the first answer back and never reaches
    /// the gate again, so it cannot open a second event after the first one was closed.
    /// </summary>
    internal static async Task<Ok<HelpResponseDto>> HelpAsync(HelpRequestBody? request, PatientContext p, IOptions<PlatformOptions> platform, IClock clock, CancellationToken ct)
    {
        var key = request?.RequestId is { } id && id != Guid.Empty ? id : (Guid?)null;
        if (key is not null && await ReplayHelpAsync(p, key.Value, platform.Value, ct).ConfigureAwait(false) is { } replay)
        {
            return TypedResults.Ok(replay);
        }

        var verdict = await p.GateAsync(null, null, "help_now", ct).ConfigureAwait(false);
        if (key is not null)
        {
            var row = HelpRequest.Record(p.PatientId, key.Value, verdict.CrisisEventId, clock);
            p.Db.HelpRequests.Add(row);
            try
            {
                await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UX_HelpRequests_PatientKey", StringComparison.Ordinal) == true)
            {
                // The same press arrived twice at once. The gate keeps one open event per patient, so both share it.
                p.Db.Entry(row).State = EntityState.Detached;
            }
        }

        return TypedResults.Ok(new HelpResponseDto(verdict.Crisis, (int)verdict.Level, verdict.Script, verdict.CrisisEventId is not null));
    }

    private static async Task<HelpResponseDto?> ReplayHelpAsync(PatientContext p, Guid key, PlatformOptions platform, CancellationToken ct)
    {
        var seen = await p.Db.HelpRequests.AsNoTracking().Where(h => h.PatientId == p.PatientId && h.RequestKey == key).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (seen is null)
        {
            return null;
        }

        var crisisEvent = seen.CrisisEventId is { } eventId
            ? await p.Db.CrisisEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId, ct).ConfigureAwait(false)
            : null;
        var level = crisisEvent?.Level ?? RiskLevel.Critical;
        var script = Scripts.Value.Render(CrisisScripts.KindFor(level), p.Language, platform.EmergencyNumber, platform.SupportLine);
        return new HelpResponseDto(true, (int)level, script, crisisEvent is { Status: not CrisisEventStatus.Closed });
    }

    // ---------- P09 ----------

    internal static async Task<Ok<HomeSummaryDto>> HomeAsync(PatientContext p, TherapistDirectory directory, IClock clock, CancellationToken ct)
    {
        var profile = await p.ProfileAsync(ct).ConfigureAwait(false);
        var nickname = await p.DecryptOptionalAsync(Fields.DisplayName, profile.DisplayNameCipher, ct).ConfigureAwait(false) ?? string.Empty;
        var now = clock.UtcNow;
        var next = await p.Db.Appointments.AsNoTracking()
            .Where(a => a.PatientId == p.PatientId && a.Status == Tamaneen.Clinical.Scheduling.AppointmentStatus.Confirmed && a.StartsAtUtc.AddMinutes(a.DurationMinutes) > now)
            .OrderBy(a => a.StartsAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        SchedulingEndpoints.AppointmentDto? nextDto = null;
        if (next is not null)
        {
            var t = (await directory.ListAsync(p.Language, [next.TherapistUserId], ct).ConfigureAwait(false)).Single();
            nextDto = new SchedulingEndpoints.AppointmentDto(next.Id, t, next.StartsAtUtc, next.DurationMinutes, next.SequenceNumber, "confirmed", next.Rescheduled,
                next.TherapistLateMinutes, null, null, false);
        }

        var today = Today(profile.TimeZone, clock);
        var items = await HomeworkItemsAsync(p, today, ct).ConfigureAwait(false);
        var due = items.FirstOrDefault(i => i.Item.DueToday && !i.Item.Completed);
        HomeworkTodayDto? homework = due is null ? null : new HomeworkTodayDto(due.Item.Id, due.Item.Title, due.Assignment.Minutes, due.DoneDays, due.Assignment.TotalDays);

        var latest = await p.Db.HomeworkCheckins.AsNoTracking().Where(c => c.PatientId == p.PatientId && c.FeedbackCipher != null)
            .OrderByDescending(c => c.FeedbackAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        FeedbackDto? feedback = null;
        if (latest is not null)
        {
            var assignment = await p.Db.HomeworkAssignments.AsNoTracking().FirstAsync(a => a.Id == latest.AssignmentId, ct).ConfigureAwait(false);
            var t = (await directory.ListAsync(p.Language, [assignment.TherapistUserId], ct).ConfigureAwait(false)).FirstOrDefault();
            feedback = new FeedbackDto(t?.DisplayName ?? string.Empty, t?.Initials ?? string.Empty, await p.DecryptAsync(Fields.CheckinFeedback, latest.FeedbackCipher!, ct).ConfigureAwait(false), latest.FeedbackAtUtc!.Value);
        }

        var dayStart = DayStartUtc(today, profile.TimeZone);
        var mood = await p.Db.MoodEntries.AsNoTracking().Where(m => m.PatientId == p.PatientId && m.RecordedAtUtc >= dayStart)
            .OrderByDescending(m => m.RecordedAtUtc).Select(m => (int?)m.Score).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var hasPlan = await p.Db.SafetyPlans.AnyAsync(s => s.PatientId == p.PatientId, ct).ConfigureAwait(false);
        var notice = await AppointmentNoticeAsync(p, clock, ct).ConfigureAwait(false);
        return TypedResults.Ok(new HomeSummaryDto(nickname, nextDto, homework, feedback, mood, hasPlan, await IntakeEndpoints.TriageStateAsync(p, ct).ConfigureAwait(false), notice));
    }

    internal static async Task<AppointmentNoticeDto?> AppointmentNoticeAsync(PatientContext p, IClock clock, CancellationToken ct)
    {
        var since = clock.UtcNow.AddDays(-7);
        var recent = await p.Db.Appointments.AsNoTracking()
            .Where(a => a.PatientId == p.PatientId
                        && ((a.Status == Tamaneen.Clinical.Scheduling.AppointmentStatus.CancelledByTherapist && a.CancelledAtUtc >= since)
                            || (a.Status == Tamaneen.Clinical.Scheduling.AppointmentStatus.NoShow && a.OutcomeRecordedAtUtc >= since)))
            .OrderByDescending(a => a.CancelledAtUtc ?? a.OutcomeRecordedAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (recent is null)
        {
            return null;
        }

        var at = recent.CancelledAtUtc ?? recent.OutcomeRecordedAtUtc;
        var rebooked = await p.Db.Appointments.AnyAsync(a => a.PatientId == p.PatientId && a.Status == Tamaneen.Clinical.Scheduling.AppointmentStatus.Confirmed && a.CreatedAtUtc >= at, ct).ConfigureAwait(false);
        return rebooked ? null : new AppointmentNoticeDto(recent.Id, recent.Missed ? "missed" : "cancelled");
    }

    // ---------- P30 ----------

    internal static async Task<Ok<MoodSaveResponse>> SaveMoodAsync(MoodEntryRequest request, PatientContext p, IClock clock, CancellationToken ct)
    {
        var note = request.Note?.Trim();
        if (note is { Length: > 1000 })
        {
            throw MeEndpoints.Invalid("note");
        }

        Gate.GateVerdict? verdict = null;
        if (!string.IsNullOrEmpty(note))
        {
            verdict = await p.GateAsync(note, null, "mood", ct).ConfigureAwait(false);
        }

        var entry = MoodEntry.Record(p.PatientId, request.Score, request.Tags ?? [], await p.EncryptOptionalAsync(Fields.MoodNote, note, ct).ConfigureAwait(false), clock);
        p.Db.MoodEntries.Add(entry);
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new MoodSaveResponse(verdict?.Crisis ?? false, (int)(verdict?.Level ?? RiskLevel.Routine), verdict is { Crisis: true } ? verdict.Script : null, Map(entry)));
    }

    internal static async Task<Ok<List<MoodEntryDto>>> MoodAsync(HttpContext http, PatientContext p, IClock clock, CancellationToken ct)
    {
        var days = int.TryParse(http.Request.Query["days"], NumberStyles.None, CultureInfo.InvariantCulture, out var d) ? Math.Clamp(d, 1, 90) : 7;
        var from = clock.UtcNow.AddDays(-days);
        var entries = await p.Db.MoodEntries.AsNoTracking().Where(m => m.PatientId == p.PatientId && m.RecordedAtUtc >= from).OrderBy(m => m.RecordedAtUtc).ToListAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(entries.Select(Map).ToList());
    }

    private static MoodEntryDto Map(MoodEntry m) => new(m.Id, m.Score, m.TagList, m.RecordedAtUtc);

    // ---------- P29, P10 ----------

    internal static async Task<Ok<HomeworkListDto>> HomeworkListAsync(PatientContext p, IClock clock, CancellationToken ct)
    {
        var profile = await p.ProfileAsync(ct).ConfigureAwait(false);
        var items = await HomeworkItemsAsync(p, Today(profile.TimeZone, clock), ct).ConfigureAwait(false);
        return TypedResults.Ok(new HomeworkListDto(items.Where(i => !i.Item.Completed).Select(i => i.Item).ToList(), items.Where(i => i.Item.Completed).Select(i => i.Item).ToList()));
    }

    internal static async Task<Ok<HomeworkDetailDto>> HomeworkDetailAsync(Guid id, PatientContext p, TherapistDirectory directory, IClock clock, CancellationToken ct)
    {
        var detail = await DetailAsync(p, directory, id, clock, markSeen: true, ct).ConfigureAwait(false);
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(detail);
    }

    internal static async Task<Ok<CheckinResponse>> CheckinAsync(Guid id, CheckinRequest request, PatientContext p, TherapistDirectory directory, IClock clock, CancellationToken ct)
    {
        var status = request.Status switch
        {
            "done" => CheckinStatus.Done,
            "partial" => CheckinStatus.Partial,
            "skipped" => CheckinStatus.Skipped,
            _ => throw MeEndpoints.Invalid("status"),
        };
        var note = request.Note?.Trim();
        if (note is { Length: > 1000 })
        {
            throw MeEndpoints.Invalid("note");
        }

        var profile = await p.ProfileAsync(ct).ConfigureAwait(false);
        var assignment = await p.Db.HomeworkAssignments.FirstOrDefaultAsync(a => a.Id == id && a.PatientId == p.PatientId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Homework {id} not found.");
        var today = Today(profile.TimeZone, clock);
        if (assignment.IsCompleted(today))
        {
            // P10: an ended exercise is read-only.
            throw new DomainException(ErrorCodes.HomeworkEnded, $"Homework {id} ended on {assignment.EndsOn}.");
        }

        // A note to the therapist is patient text like any other: it passes the crisis gate first.
        Gate.GateVerdict? verdict = null;
        if (!string.IsNullOrEmpty(note))
        {
            verdict = await p.GateAsync(note, null, "homework", ct).ConfigureAwait(false);
        }

        var checkin = await p.Db.HomeworkCheckins.FirstOrDefaultAsync(c => c.AssignmentId == assignment.Id && c.Day == today, ct).ConfigureAwait(false);
        if (checkin is null)
        {
            checkin = HomeworkCheckin.Start(assignment.Id, p.PatientId, today, clock);
            p.Db.HomeworkCheckins.Add(checkin);
        }

        checkin.Report(status, status == CheckinStatus.Skipped ? null : request.Feeling, await p.EncryptOptionalAsync(Fields.CheckinNote, note, ct).ConfigureAwait(false), clock);
        checkin.SetGateLevel(verdict?.Level);
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        var detail = await DetailAsync(p, directory, id, clock, markSeen: false, ct).ConfigureAwait(false);
        return TypedResults.Ok(new CheckinResponse(verdict?.Crisis ?? false, (int)(verdict?.Level ?? RiskLevel.Routine), verdict is { Crisis: true } ? verdict.Script : null, detail));
    }

    private sealed record HomeworkRow(HomeworkItemDto Item, HomeworkAssignment Assignment, int DoneDays);

    private static async Task<List<HomeworkRow>> HomeworkItemsAsync(PatientContext p, DateOnly today, CancellationToken ct)
    {
        var assignments = await p.Db.HomeworkAssignments.AsNoTracking().Where(a => a.PatientId == p.PatientId).OrderBy(a => a.EndsOn).ToListAsync(ct).ConfigureAwait(false);
        if (assignments.Count == 0)
        {
            return [];
        }

        var ids = assignments.Select(a => a.Id).ToList();
        var checkins = await p.Db.HomeworkCheckins.AsNoTracking().Where(c => ids.Contains(c.AssignmentId)).ToListAsync(ct).ConfigureAwait(false);
        var therapists = assignments.Select(a => a.TherapistUserId).Distinct().ToList();
        var names = await p.Db.StaffProfiles.AsNoTracking().Where(s => therapists.Contains(s.UserId)).ToDictionaryAsync(s => s.UserId, s => s.DisplayName(p.Language), ct).ConfigureAwait(false);
        // Gulf working week: Sunday to Thursday.
        var weekStart = today.AddDays(-(int)today.DayOfWeek);
        var rows = new List<HomeworkRow>();
        foreach (var a in assignments)
        {
            var mine = checkins.Where(c => c.AssignmentId == a.Id).ToList();
            var doneThisWeek = mine.Count(c => c.Day >= weekStart && c.Status != CheckinStatus.Skipped);
            var doneDays = mine.Count(c => c.Status != CheckinStatus.Skipped);
            var hasToday = mine.Any(c => c.Day == today);
            var newFeedback = mine.Any(c => c.FeedbackCipher is not null && c.FeedbackSeenAtUtc is null);
            var item = new HomeworkItemDto(
                a.Id,
                await p.DecryptAsync(Fields.HomeworkTitle, a.TitleCipher, ct).ConfigureAwait(false),
                names.GetValueOrDefault(a.TherapistUserId, string.Empty),
                FrequencyCode(a.Frequency),
                a.EndsOn,
                a.DueOn(today, doneThisWeek) && !hasToday,
                a.Frequency == HomeworkFrequency.Daily ? doneDays : doneThisWeek,
                newFeedback,
                a.IsCompleted(today));
            rows.Add(new HomeworkRow(item, a, doneDays));
        }

        return rows;
    }

    private static async Task<HomeworkDetailDto> DetailAsync(PatientContext p, TherapistDirectory directory, Guid id, IClock clock, bool markSeen, CancellationToken ct)
    {
        var profile = await p.ProfileAsync(ct).ConfigureAwait(false);
        var today = Today(profile.TimeZone, clock);
        var row = (await HomeworkItemsAsync(p, today, ct).ConfigureAwait(false)).FirstOrDefault(r => r.Item.Id == id)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Homework {id} not found.");
        var instructions = await p.DecryptJsonAsync<List<string>>(Fields.HomeworkInstructions, row.Assignment.InstructionsCipher, ct).ConfigureAwait(false) ?? [];
        var checkins = await p.Db.HomeworkCheckins.Where(c => c.AssignmentId == id).ToListAsync(ct).ConfigureAwait(false);
        var withFeedback = checkins.Where(c => c.FeedbackCipher is not null).OrderByDescending(c => c.FeedbackAtUtc).FirstOrDefault();
        LatestFeedbackDto? feedback = null;
        if (withFeedback is not null)
        {
            var t = (await directory.ListAsync(p.Language, [row.Assignment.TherapistUserId], ct).ConfigureAwait(false)).FirstOrDefault();
            feedback = new LatestFeedbackDto(await p.DecryptAsync(Fields.CheckinFeedback, withFeedback.FeedbackCipher!, ct).ConfigureAwait(false), withFeedback.FeedbackAtUtc!.Value, t?.Initials ?? string.Empty);
            if (markSeen)
            {
                foreach (var c in checkins.Where(c => c.FeedbackCipher is not null))
                {
                    c.MarkFeedbackSeen(clock);
                }
            }
        }

        var todayCheckin = checkins.FirstOrDefault(c => c.Day == today);
        TodayCheckinDto? todayDto = todayCheckin is null ? null : new TodayCheckinDto(
            todayCheckin.Status switch { CheckinStatus.Done => "done", CheckinStatus.Partial => "partial", _ => "skipped" },
            todayCheckin.Feeling,
            await p.DecryptOptionalAsync(Fields.CheckinNote, todayCheckin.NoteCipher, ct).ConfigureAwait(false));
        var i = row.Item;
        return new HomeworkDetailDto(i.Id, i.Title, i.TherapistName, i.Frequency, i.EndsOn, i.DueToday, i.DoneThisPeriod, i.HasNewFeedback, i.Completed,
            instructions, row.Assignment.Minutes, row.DoneDays, row.Assignment.TotalDays, feedback, todayDto);
    }

    internal static string FrequencyCode(HomeworkFrequency f) => f switch
    {
        HomeworkFrequency.Daily => "daily",
        HomeworkFrequency.TwiceWeekly => "twice_weekly",
        HomeworkFrequency.Weekly => "weekly",
        _ => "once",
    };

    // ---------- P37 ----------

    /// <summary>The ETag is the plan's version (S35/P37 concurrency rule): a save that sends If-Match must name the current one.</summary>
    internal static async Task<Ok<SafetyPlanDto>> SafetyPlanAsync(HttpContext http, PatientContext p, CancellationToken ct)
    {
        var plan = await PlanAsync(p, ct).ConfigureAwait(false);
        CareRecords.SendVersion(http, plan.Version.ToString(CultureInfo.InvariantCulture));
        return TypedResults.Ok(plan);
    }

    private static async Task<SafetyPlanDto> PlanAsync(PatientContext p, CancellationToken ct)
    {
        var plan = await p.Db.SafetyPlans.AsNoTracking().FirstOrDefaultAsync(s => s.PatientId == p.PatientId, ct).ConfigureAwait(false);
        var sections = SafetyPlan.Sections.ToDictionary(s => s, _ => string.Empty);
        if (plan is null)
        {
            return new SafetyPlanDto(sections, [], null, null);
        }

        var stored = ReadStoredPlan(await p.DecryptAsync(Fields.SafetyPlan, plan.SectionsCipher, ct).ConfigureAwait(false));
        foreach (var (k, v) in stored.Sections.Where(kv => sections.ContainsKey(kv.Key)))
        {
            sections[k] = v;
        }

        string? coAuthor = null;
        if (plan.CoAuthorTherapistId is { } therapist)
        {
            coAuthor = await p.Db.StaffProfiles.AsNoTracking().Where(s => s.UserId == therapist).Select(s => p.Language == "en" ? s.DisplayNameEn : s.DisplayNameAr).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        }

        return new SafetyPlanDto(sections, stored.Contacts ?? [], plan.UpdatedAtUtc, coAuthor, Version: plan.Version);
    }

    internal static StoredSafetyPlan ReadStoredPlan(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("sections", out var nested) && nested.ValueKind == JsonValueKind.Object)
        {
            var plan = doc.RootElement.Deserialize<StoredSafetyPlan>(PatientContext.Json);
            return new StoredSafetyPlan(plan?.Sections ?? [], plan?.Contacts ?? []);
        }

        return new StoredSafetyPlan(doc.RootElement.Deserialize<Dictionary<string, string>>(PatientContext.Json) ?? [], []);
    }

    /// <summary>
    /// P37 save. If-Match is optional here (a write queued offline before versions existed has none): when sent it must
    /// name the current version, else 412 and the app shows the plan the therapist saved meanwhile. Each save adds a
    /// version and tells the treating therapist.
    /// </summary>
    internal static async Task<Ok<SafetyPlanDto>> UpdateSafetyPlanAsync(UpdateSafetyPlanRequest request, HttpContext http, PatientContext p, CareRecords care, ICurrentUser user, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var (sections, contacts) = CleanPlan(request);
        var current = await p.Db.SafetyPlans.AsNoTracking().Where(s => s.PatientId == p.PatientId).Select(s => (int?)s.Version).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? 0;
        CareRecords.CheckVersionIfPresent(http, current.ToString(CultureInfo.InvariantCulture));
        var all = string.Join("\n", sections.Values.Concat(contacts.Select(c => c.Name)).Where(v => v.Length > 0));
        Gate.GateVerdict? verdict = null;
        if (all.Length > 0)
        {
            verdict = await p.GateAsync(all, null, "safety_plan", ct).ConfigureAwait(false);
        }

        var plan = await p.Db.SafetyPlans.FirstOrDefaultAsync(s => s.PatientId == p.PatientId, ct).ConfigureAwait(false);
        if (plan is null)
        {
            plan = SafetyPlan.Create(p.PatientId);
            p.Db.SafetyPlans.Add(plan);
        }

        plan.Update(await p.EncryptJsonAsync(Fields.SafetyPlan, new StoredSafetyPlan(sections, contacts), ct).ConfigureAwait(false), user.UserId!.Value, user.Role!, clock);
        p.Db.SafetyPlanVersions.Add(SafetyPlanVersion.Of(plan));
        audit.Append("safety_plan.updated", "SafetyPlan", null, (await p.AccountAsync(ct).ConfigureAwait(false)).PseudonymId, $"version={plan.Version}");
        await SavePlanOrConflictAsync(p.Db, ct).ConfigureAwait(false);
        foreach (var therapist in await care.ActiveTherapistsAsync(p.PatientId, ct).ConfigureAwait(false))
        {
            await care.NotifyAsync(therapist, "safety_plan.updated", new Dictionary<string, string> { ["pseudonymId"] = (await p.AccountAsync(ct).ConfigureAwait(false)).PseudonymId, ["version"] = plan.Version.ToString(CultureInfo.InvariantCulture), ["by"] = "patient" },
                $"/patients/{(await p.AccountAsync(ct).ConfigureAwait(false)).PseudonymId}/safety-plan", ct).ConfigureAwait(false);
        }

        var saved = await PlanAsync(p, ct).ConfigureAwait(false);
        CareRecords.SendVersion(http, saved.Version.ToString(CultureInfo.InvariantCulture));
        return TypedResults.Ok(saved with { Crisis = verdict?.Crisis ?? false, Script = verdict is { Crisis: true } ? verdict.Script : null });
    }

    /// <summary>Sections (up to 1500 characters each) and up to five contacts with a valid phone.</summary>
    internal static (Dictionary<string, string> Sections, List<PlanContactDto> Contacts) CleanPlan(UpdateSafetyPlanRequest request)
    {
        var sections = SafetyPlan.Sections.ToDictionary(s => s, s => (request.Sections ?? []).GetValueOrDefault(s)?.Trim() ?? string.Empty);
        if (sections.Values.Any(v => v.Length > 1500))
        {
            throw MeEndpoints.Invalid("sections");
        }

        return (sections, CleanContacts(request.Contacts));
    }

    /// <summary>Two saves of the same version at once (patient and therapist): the second gets 412.</summary>
    internal static async Task SavePlanOrConflictAsync(Tamaneen.Infrastructure.Persistence.TamaneenDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException(ErrorCodes.VersionChanged, "The safety plan changed concurrently.");
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_SafetyPlanVersions_PatientId_Version", StringComparison.Ordinal) == true
            || ex.InnerException?.Message.Contains("PK_SafetyPlans", StringComparison.Ordinal) == true)
        {
            throw new DomainException(ErrorCodes.VersionChanged, "The safety plan changed concurrently.");
        }
    }

    /// <summary>Each contact needs a name (up to 100 characters) and a valid phone number; at most five.</summary>
    internal static List<PlanContactDto> CleanContacts(List<PlanContactDto>? contacts)
    {
        var list = contacts ?? [];
        if (list.Count > MaxPlanContacts)
        {
            throw MeEndpoints.Invalid("contacts");
        }

        return list.Select((c, i) =>
        {
            var name = c?.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > MaxPlanContactName)
            {
                throw MeEndpoints.Invalid($"contacts[{i}].name");
            }

            var phone = MeEndpoints.CleanPhone(c!.Phone, $"contacts[{i}].phone") ?? throw MeEndpoints.Invalid($"contacts[{i}].phone");
            return new PlanContactDto(name, phone);
        }).ToList();
    }

    // ---------- time helpers ----------

    internal static DateOnly Today(string timeZone, IClock clock) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(timeZone)).DateTime);

    internal static DateTimeOffset DayStartUtc(DateOnly day, string timeZone)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
