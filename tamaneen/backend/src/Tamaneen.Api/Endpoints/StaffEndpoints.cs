using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamaneen.Api.Clinical;
using Tamaneen.Api.Gate;
using Tamaneen.Api.Patients;
using Tamaneen.Clinical.Care;
using Tamaneen.Clinical.Intake;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Hosting;
using Tamaneen.Identity;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.Infrastructure.Security;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Endpoints;

/// <summary>
/// Staff portal endpoints (S01 and what the clinical flow needs around it). Queries run as the signed-in
/// clinician, so a therapist only ever sees patients assigned to them (row-level security).
/// </summary>
public static class StaffEndpoints
{
    public sealed record StaffMeDto(Guid UserId, string DisplayName, string Role, string Language, bool OnCall);

    public sealed record PendingProfileDto(string PseudonymId, int RiskLevel, int? Phq9, int? Gad7, DateTimeOffset? FirstSessionAt);

    public sealed record TodaySessionDto(Guid AppointmentId, DateTimeOffset StartsAt, string PseudonymId, int SequenceNumber, int? RiskLevel, bool OutcomeRecorded, bool HasNewCheckins);

    public sealed record StatsDto(int TodaySessions, int PendingProfiles, int NewCheckins, int CapacityUsed, int CapacityMax, int MyPatients);

    public sealed record DashboardDto(string DisplayName, string Date, JsonElement Alerts, StatsDto Stats, IReadOnlyList<PendingProfileDto> PendingProfiles, IReadOnlyList<TodaySessionDto> TodaySessions);

    public sealed record IntakeSectionsDto(string Concern, string Goals, string Supports, string PriorCare, string Extra);

    public sealed record StaffIntakeViewDto(string PseudonymId, int RiskLevel, int? Phq9, int? Gad7, bool? CssrsPositive, IntakeSectionsDto Sections, DateTimeOffset? SubmittedAt, bool Approved);

    public sealed record OutcomeRequest(string? Outcome);

    public sealed record TriageItemDto(Guid Id, string PseudonymId, string Path, int RiskLevel, int? Phq9, int? Gad7, bool? CssrsPositive, DateTimeOffset SubmittedAt, DateTimeOffset DueAt);

    public sealed record ReviewRequest(int RiskLevel, List<string>? Focus);

    public sealed record AssignHomeworkRequest(string? Title, List<string>? Instructions, int? Minutes, string? Frequency, DateOnly StartsOn, DateOnly EndsOn);

    public sealed record FeedbackRequest(string? Text);

    public static IEndpointRouteBuilder MapStaffEndpoints(this IEndpointRouteBuilder app)
    {
        var staff = app.MapGroup("/v1/staff").RequireAuthorization(p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Therapist, AppRoles.Supervisor, AppRoles.ClinicalLead, AppRoles.Admin));
        staff.MapGet("/me", MeAsync);

        var clinical = app.MapGroup("/v1/staff").RequireAuthorization(AuthSetup.ClinicalPolicy);
        clinical.MapGet("/dashboard", DashboardAsync);
        clinical.MapGet("/patients/{pseudonymId}/intake", IntakeViewAsync);
        clinical.MapPost("/patients/{pseudonymId}/intake/approve", ApproveAsync);
        clinical.MapPost("/appointments/{id:guid}/outcome", OutcomeAsync);
        clinical.MapPost("/patients/{pseudonymId}/homework", AssignHomeworkAsync);
        clinical.MapPost("/checkins/{id:guid}/feedback", FeedbackAsync);

        var supervisors = app.MapGroup("/v1/staff/triage").RequireAuthorization(AuthSetup.CrisisCloserPolicy);
        supervisors.MapGet("", TriageListAsync);
        supervisors.MapPost("/{id:guid}/review", ReviewAsync);
        return app;
    }

    internal static async Task<Ok<StaffMeDto>> MeAsync(ICurrentUser user, TamaneenDbContext db, CancellationToken ct)
    {
        var account = await db.UserAccounts.AsNoTracking().FirstAsync(a => a.Id == user.UserId, ct).ConfigureAwait(false);
        var profile = await db.StaffProfiles.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == account.Id, ct).ConfigureAwait(false);
        return TypedResults.Ok(new StaffMeDto(account.Id, profile?.DisplayName(user.Language) ?? account.PseudonymId, RoleCode(account.Role), account.PreferredLanguage, profile?.OnCallTier is not null));
    }

    internal static string RoleCode(string role) => role switch
    {
        AppRoles.Therapist => "therapist",
        AppRoles.Supervisor => "supervisor",
        AppRoles.ClinicalLead => "lead",
        _ => "admin",
    };

    // ---------- S01 ----------

    internal static async Task<Ok<DashboardDto>> DashboardAsync(HttpContext http, ICurrentUser user, TamaneenDbContext db, IHttpClientFactory httpClients, IOptions<CrisisGateOptions> gate, IOptions<PlatformOptions> platform, IClock clock, CancellationToken ct)
    {
        var me = user.UserId!.Value;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(platform.Value.TimeZone);
        var localNow = TimeZoneInfo.ConvertTime(clock.UtcNow, zone);
        var today = DateOnly.FromDateTime(localNow.DateTime);
        var dayStart = CareEndpoints.DayStartUtc(today, platform.Value.TimeZone);
        var dayEnd = dayStart.AddDays(1);
        var name = await db.StaffProfiles.AsNoTracking().Where(s => s.UserId == me).Select(s => user.Language == "en" ? s.DisplayNameEn : s.DisplayNameAr).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;

        var assignments = await db.CareAssignments.AsNoTracking().Where(a => a.TherapistUserId == me && a.EndedAtUtc == null).Select(a => a.PatientId).ToListAsync(ct).ConfigureAwait(false);
        var pseudonyms = await db.UserAccounts.AsNoTracking().Where(a => assignments.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.PseudonymId, ct).ConfigureAwait(false);
        var triage = (await db.TriageCases.AsNoTracking().Where(t => assignments.Contains(t.PatientId)).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(t => t.PatientId).ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.SubmittedAtUtc).First());
        var intakes = (await db.IntakeSessions.AsNoTracking().Where(i => assignments.Contains(i.PatientId) && i.Status == IntakeStatus.Submitted).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(i => i.PatientId).ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.SubmittedAtUtc).First());
        var appointments = await db.Appointments.AsNoTracking().Where(a => a.TherapistUserId == me && a.StartsAtUtc >= clock.UtcNow.AddDays(-1)).OrderBy(a => a.StartsAtUtc).ToListAsync(ct).ConfigureAwait(false);
        var weekAgo = clock.UtcNow.AddDays(-7);
        var recentCheckins = await db.HomeworkCheckins.AsNoTracking().Where(c => assignments.Contains(c.PatientId) && c.UpdatedAtUtc >= weekAgo).Select(c => c.PatientId).ToListAsync(ct).ConfigureAwait(false);
        // A recent crisis raises the risk shown for a patient, whatever the triage said.
        var monthAgo = clock.UtcNow.AddDays(-30);
        var crisisLevels = (await db.CrisisEvents.AsNoTracking().Where(e => assignments.Contains(e.PatientId) && (e.Status != Tamaneen.Crisis.CrisisEventStatus.Closed || e.OpenedAtUtc >= monthAgo))
            .Select(e => new { e.PatientId, e.Level }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(e => e.PatientId).ToDictionary(g => g.Key, g => g.Max(e => e.Level));
        RiskLevel Risk(Guid pid) => (RiskLevel)Math.Max((int)(triage.TryGetValue(pid, out var t) ? t.RiskLevel : RiskLevel.Routine), (int)crisisLevels.GetValueOrDefault(pid));
        var capacity = await db.TherapistProfiles.AsNoTracking().Where(t => t.UserId == me).Select(t => t.Capacity).FirstOrDefaultAsync(ct).ConfigureAwait(false);

        // Profiles waiting for this therapist's approval: submitted intake, or a direct booking with no intake.
        var pending = assignments
            .Where(pid => intakes.TryGetValue(pid, out var i) ? i.TherapistApprovedAtUtc is null : triage.TryGetValue(pid, out var t) && t.Path == "direct" && t.ReviewedAtUtc is null)
            .Select(pid =>
            {
                triage.TryGetValue(pid, out var t);
                var first = appointments.FirstOrDefault(a => a.PatientId == pid && a.Status == AppointmentStatus.Confirmed && a.StartsAtUtc > clock.UtcNow);
                return new PendingProfileDto(pseudonyms[pid], (int)Risk(pid), t?.Phq9, t?.Gad7, first?.StartsAtUtc);
            }).ToList();

        var todays = appointments.Where(a => a.StartsAtUtc >= dayStart && a.StartsAtUtc < dayEnd && a.Status is AppointmentStatus.Confirmed or AppointmentStatus.Completed or AppointmentStatus.NoShow)
            .Select(a => new TodaySessionDto(a.Id, a.StartsAtUtc, pseudonyms.GetValueOrDefault(a.PatientId, string.Empty), a.SequenceNumber,
                (int)Risk(a.PatientId), a.OutcomeRecordedAtUtc is not null, recentCheckins.Contains(a.PatientId)))
            .ToList();

        var alerts = await OpenAlertsAsync(http, httpClients, gate.Value, ct).ConfigureAwait(false);
        return TypedResults.Ok(new DashboardDto(
            name,
            today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            alerts,
            new StatsDto(todays.Count, pending.Count, recentCheckins.Count, assignments.Count, capacity, assignments.Count),
            pending,
            todays));
    }

    /// <summary>
    /// Open crisis events visible to this clinician, as the crisis worker formats them. The call carries the
    /// clinician's own token, so the worker's row-level security limits a therapist to their patients.
    /// </summary>
    private static async Task<JsonElement> OpenAlertsAsync(HttpContext http, IHttpClientFactory factory, CrisisGateOptions gate, CancellationToken ct)
    {
        try
        {
            using var client = factory.CreateClient("crisis-console");
            client.Timeout = TimeSpan.FromSeconds(5);
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(gate.BaseUrl), "v1/crisis/events/open"));
            if (AuthenticationHeaderValue.TryParse(http.Request.Headers.Authorization, out var auth))
            {
                request.Headers.Authorization = auth;
            }

            request.Headers.AcceptLanguage.ParseAdd(LocalizationSetup.RequestLanguage(http));
            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).RootElement.Clone();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // The live hub still delivers alerts; the dashboard shows the rest.
        }

        return JsonDocument.Parse("[]").RootElement.Clone();
    }

    internal static async Task<Ok<StaffIntakeViewDto>> IntakeViewAsync(string pseudonymId, TamaneenDbContext db, IPatientDataProtector protector, IAuditLog audit, CancellationToken ct)
    {
        var patientId = await PatientIdAsync(db, pseudonymId, ct).ConfigureAwait(false);
        var triage = await db.TriageCases.AsNoTracking().Where(t => t.PatientId == patientId).OrderByDescending(t => t.SubmittedAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, "No file for this patient.");
        var intake = triage.IntakeId is { } iid ? await db.IntakeSessions.AsNoTracking().FirstOrDefaultAsync(i => i.Id == iid, ct).ConfigureAwait(false) : null;
        var sections = new IntakeSectionsDto(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        if (intake?.SummaryCipher is { } cipher)
        {
            var stored = JsonSerializer.Deserialize<IntakeEndpoints.StoredSummary>(await protector.DecryptAsync(patientId, Fields.IntakeSummary, cipher, ct).ConfigureAwait(false), PatientContext.Json);
            string S(string k) => stored?.Sections.GetValueOrDefault(k)?.Text ?? string.Empty;
            sections = new IntakeSectionsDto(S("concern"), S("goals"), S("supports"), S("priorCare"), S("extra"));
        }

        if (triage.AddendaCipher is { } addenda)
        {
            var extra = await protector.DecryptAsync(patientId, Fields.TriageAddenda, addenda, ct).ConfigureAwait(false);
            sections = sections with { Extra = string.IsNullOrEmpty(sections.Extra) ? extra : sections.Extra + "\n" + extra };
        }

        var screening = await db.Screenings.AsNoTracking().Where(s => s.PatientId == patientId).OrderByDescending(s => s.CompletedAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        audit.Append("patient.file_opened", "TriageCase", triage.Id.ToString(), pseudonymId);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var crisisLevel = await db.CrisisEvents.AsNoTracking().Where(e => e.PatientId == patientId).Select(e => (int?)e.Level).MaxAsync(ct).ConfigureAwait(false) ?? 0;
        return TypedResults.Ok(new StaffIntakeViewDto(pseudonymId, Math.Max((int)triage.RiskLevel, crisisLevel), triage.Phq9, triage.Gad7, screening?.Positive ?? triage.CssrsPositive, sections, triage.SubmittedAtUtc,
            intake?.TherapistApprovedAtUtc is not null || (intake is null && triage.ReviewedAtUtc is not null)));
    }

    internal static async Task<NoContent> ApproveAsync(string pseudonymId, ICurrentUser user, TamaneenDbContext db, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var patientId = await PatientIdAsync(db, pseudonymId, ct).ConfigureAwait(false);
        var intake = await db.IntakeSessions.Where(i => i.PatientId == patientId && i.Status == IntakeStatus.Submitted).OrderByDescending(i => i.SubmittedAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (intake is not null)
        {
            intake.ApproveByTherapist(user.UserId!.Value, clock);
        }
        else
        {
            // Direct booking: approving the file is reviewing the triage case.
            var triage = await db.TriageCases.Where(t => t.PatientId == patientId).OrderByDescending(t => t.SubmittedAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false)
                ?? throw new DomainException(ErrorCodes.NotFound, "No file for this patient.");
            triage.ApproveByTherapist(user.UserId!.Value, clock);
        }

        audit.Append("patient.file_approved", "UserAccount", patientId.ToString(), pseudonymId);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    internal static async Task<NoContent> OutcomeAsync(Guid id, OutcomeRequest request, ICurrentUser user, TamaneenDbContext db, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var outcome = request.Outcome switch
        {
            "attended" => AppointmentStatus.Completed,
            "no_show" => AppointmentStatus.NoShow,
            "cancelled" => AppointmentStatus.CancelledByTherapist,
            _ => throw MeEndpoints.Invalid("outcome"),
        };
        var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.TherapistUserId == user.UserId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Appointment {id} not found.");
        appointment.RecordOutcome(outcome, clock);
        if (outcome == AppointmentStatus.CancelledByTherapist)
        {
            (await db.AvailabilitySlots.FirstAsync(s => s.Id == appointment.SlotId, ct).ConfigureAwait(false)).Free();
        }

        var pseudonym = await db.UserAccounts.Where(a => a.Id == appointment.PatientId).Select(a => a.PseudonymId).FirstAsync(ct).ConfigureAwait(false);
        audit.Append("appointment.outcome_recorded", "Appointment", appointment.Id.ToString(), pseudonym, request.Outcome);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    internal static async Task<Created> AssignHomeworkAsync(string pseudonymId, AssignHomeworkRequest request, ICurrentUser user, TamaneenDbContext db, IPatientDataProtector protector, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200 || request.Instructions is not { Count: > 0 and <= 20 } || request.Instructions.Any(i => string.IsNullOrWhiteSpace(i) || i.Length > 500))
        {
            throw MeEndpoints.Invalid("homework");
        }

        var frequency = request.Frequency switch
        {
            "daily" => HomeworkFrequency.Daily,
            "twice_weekly" => HomeworkFrequency.TwiceWeekly,
            "weekly" => HomeworkFrequency.Weekly,
            "once" => HomeworkFrequency.Once,
            _ => throw MeEndpoints.Invalid("frequency"),
        };
        var patientId = await PatientIdAsync(db, pseudonymId, ct).ConfigureAwait(false);
        var assignment = HomeworkAssignment.Assign(
            patientId,
            user.UserId!.Value,
            await protector.EncryptAsync(patientId, Fields.HomeworkTitle, request.Title.Trim(), ct).ConfigureAwait(false),
            await protector.EncryptAsync(patientId, Fields.HomeworkInstructions, JsonSerializer.Serialize(request.Instructions.Select(i => i.Trim()).ToList(), PatientContext.Json), ct).ConfigureAwait(false),
            request.Minutes is > 0 and <= 240 ? request.Minutes : null,
            frequency,
            request.StartsOn,
            request.EndsOn,
            clock);
        db.HomeworkAssignments.Add(assignment);
        audit.Append("homework.assigned", "HomeworkAssignment", assignment.Id.ToString(), pseudonymId);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Created($"/v1/homework/{assignment.Id}");
    }

    internal static async Task<NoContent> FeedbackAsync(Guid id, FeedbackRequest request, TamaneenDbContext db, IPatientDataProtector protector, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 1000)
        {
            throw MeEndpoints.Invalid("text");
        }

        var checkin = await db.HomeworkCheckins.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Check-in {id} not found.");
        checkin.GiveFeedback(await protector.EncryptAsync(checkin.PatientId, Fields.CheckinFeedback, request.Text.Trim(), ct).ConfigureAwait(false), clock);
        var pseudonym = await db.UserAccounts.Where(a => a.Id == checkin.PatientId).Select(a => a.PseudonymId).FirstAsync(ct).ConfigureAwait(false);
        audit.Append("homework.feedback_given", "HomeworkCheckin", checkin.Id.ToString(), pseudonym);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    // ---------- Triage review (supervisors and the clinical lead) ----------

    internal static async Task<Ok<List<TriageItemDto>>> TriageListAsync(TamaneenDbContext db, CancellationToken ct)
    {
        var cases = await db.TriageCases.AsNoTracking().Where(t => t.Status == TriageStatus.Pending).OrderByDescending(t => t.RiskLevel).ThenBy(t => t.DueAtUtc).Take(200).ToListAsync(ct).ConfigureAwait(false);
        var ids = cases.Select(c => c.PatientId).ToList();
        var pseudonyms = await db.UserAccounts.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.PseudonymId, ct).ConfigureAwait(false);
        return TypedResults.Ok(cases.Select(c => new TriageItemDto(c.Id, pseudonyms[c.PatientId], c.Path, (int)c.RiskLevel, c.Phq9, c.Gad7, c.CssrsPositive, c.SubmittedAtUtc, c.DueAtUtc)).ToList());
    }

    /// <summary>The supervisor confirms the risk level and the focus areas; the platform proposes three therapists.</summary>
    internal static async Task<Ok<TriageItemDto>> ReviewAsync(Guid id, ReviewRequest request, ICurrentUser user, TamaneenDbContext db, TherapistDirectory directory, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (request.RiskLevel is < 1 or > 4 || request.Focus is not { Count: > 0 } || request.Focus.Any(f => !TherapistProfile.SpecialtyCodes.Contains(f)))
        {
            throw MeEndpoints.Invalid("review");
        }

        var triage = await db.TriageCases.FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Triage {id} not found.");
        triage.Review(user.UserId!.Value, (RiskLevel)request.RiskLevel, clock);
        var patient = await db.UserAccounts.AsNoTracking().FirstAsync(a => a.Id == triage.PatientId, ct).ConfigureAwait(false);
        var suggestions = await directory.SuggestAsync(request.Focus, patient.PreferredLanguage, 3, ct).ConfigureAwait(false);
        var existing = await db.MatchSuggestions.Where(m => m.PatientId == triage.PatientId).ToListAsync(ct).ConfigureAwait(false);
        db.MatchSuggestions.RemoveRange(existing);
        var rank = 1;
        foreach (var (therapistId, shared) in suggestions)
        {
            db.MatchSuggestions.Add(MatchSuggestion.Create(triage.PatientId, therapistId, rank++, shared, clock));
        }

        audit.Append("triage.reviewed", "TriageCase", triage.Id.ToString(), patient.PseudonymId, $"risk={request.RiskLevel};suggestions={suggestions.Count}");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new TriageItemDto(triage.Id, patient.PseudonymId, triage.Path, (int)triage.RiskLevel, triage.Phq9, triage.Gad7, triage.CssrsPositive, triage.SubmittedAtUtc, triage.DueAtUtc));
    }

    private static async Task<Guid> PatientIdAsync(TamaneenDbContext db, string pseudonymId, CancellationToken ct)
    {
        if (!PseudonymId.TryParse(pseudonymId, out var parsed))
        {
            throw new DomainException(ErrorCodes.InvalidPseudonym, "Bad pseudonym.");
        }

        var id = await db.UserAccounts.AsNoTracking().Where(a => a.PseudonymId == parsed.Value && a.Role == AppRoles.Patient).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);

        // A patient the caller has no access to is indistinguishable from one that does not exist.
        if (id is null || !await db.PatientProfiles.AnyAsync(p => p.PatientId == id, ct).ConfigureAwait(false))
        {
            throw new DomainException(ErrorCodes.NotFound, "Patient not visible.");
        }

        return id.Value;
    }
}
