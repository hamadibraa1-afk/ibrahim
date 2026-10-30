using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Tamaneen.Api.Auth;
using Tamaneen.Api.Clinical;
using Tamaneen.Api.Patients;
using Tamaneen.Clinical;
using Tamaneen.Clinical.Intake;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Hosting;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;
using Tamaneen.SharedKernel.Notifications;
using Tamaneen.Api.Notifications;

namespace Tamaneen.Api.Endpoints;

/// <summary>Matches (P07), directory (P24, P25), booking (P08, P26) and appointments (P27, P28).</summary>
public static class SchedulingEndpoints
{
    /// <summary>Identity recorded when a booking creates the care assignment.</summary>
    public static readonly Guid SchedulingServiceId = new("9b3f6a1e-2c4d-4e8f-a7b1-0c5d2e9f8a63");

    public sealed record MatchReasonsDto(IReadOnlyList<string> SharedSpecialties, bool SpeaksYourLanguage, bool EveningSlots, DateTimeOffset? NextAvailableAt);

    public sealed record MatchSuggestionDto(TherapistSummaryDto Therapist, int Rank, bool Best, MatchReasonsDto Reasons);

    public sealed record MatchListDto(string Status, IReadOnlyList<MatchSuggestionDto> Items);

    public sealed record SlotDto(Guid SlotId, DateTimeOffset StartsAt);

    public sealed record HoldRequest(Guid SlotId);

    public sealed record HoldDto(Guid HoldId, Guid SlotId, DateTimeOffset StartsAt, DateTimeOffset ExpiresAt, TherapistSummaryDto Therapist);

    public sealed record AppointmentDto(Guid Id, TherapistSummaryDto Therapist, DateTimeOffset StartsAt, int DurationMinutes, int SequenceNumber, string Status, bool Rescheduled,
        int? TherapistLateMinutes, string? CancelledBy, string? CancelReasonCode, bool Missed);

    public sealed record AppointmentDetailDto(Guid Id, TherapistSummaryDto Therapist, DateTimeOffset StartsAt, int DurationMinutes, int SequenceNumber, string Status, bool Rescheduled, bool CanChange, DateTimeOffset ChangeDeadline, string? JoinUrl, DateTimeOffset JoinAvailableAt,
        int? TherapistLateMinutes, string? CancelledBy, string? CancelReasonCode, bool Missed);

    public sealed record AppointmentListDto(IReadOnlyList<AppointmentDto> Upcoming, IReadOnlyList<AppointmentDto> Past);

    public sealed record RescheduleRequest(Guid SlotId);

    public sealed record CancelRequest(string? ReasonCode);

    public sealed record DismissRequest(string? Reason);

    public static IEndpointRouteBuilder MapSchedulingEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1").RequireAuthorization(AuthSetup.PatientPolicy);
        g.MapGet("/matches", MatchesAsync);
        g.MapPost("/matches/{therapistId:guid}/dismiss", DismissAsync);
        g.MapGet("/therapists", DirectoryAsync);
        g.MapGet("/therapists/{id:guid}", async (Guid id, PatientContext p, TherapistDirectory directory, CancellationToken ct) => TypedResults.Ok(await directory.GetAsync(id, p.Language, ct).ConfigureAwait(false)));
        g.MapGet("/therapists/{id:guid}/slots", SlotsAsync);
        g.MapPost("/bookings/holds", HoldAsync);
        g.MapPost("/bookings/holds/{holdId:guid}/confirm", ConfirmAsync);
        g.MapDelete("/bookings/holds/{holdId:guid}", ReleaseAsync);
        g.MapGet("/appointments", AppointmentsAsync);
        g.MapGet("/appointments/{id:guid}", DetailAsync);
        g.MapGet("/appointments/{id:guid}/calendar", CalendarAsync);
        g.MapPost("/appointments/{id:guid}/cancel", CancelAsync);
        g.MapPost("/appointments/{id:guid}/reschedule", RescheduleAsync);
        return app;
    }

    // ---------- P07 ----------

    internal static async Task<Ok<MatchListDto>> MatchesAsync(PatientContext p, TherapistDirectory directory, CancellationToken ct) =>
        TypedResults.Ok(await BuildMatchesAsync(p, directory, ct).ConfigureAwait(false));

    private static async Task<MatchListDto> BuildMatchesAsync(PatientContext p, TherapistDirectory directory, CancellationToken ct)
    {
        var triage = await p.Db.TriageCases.AsNoTracking().Where(t => t.PatientId == p.PatientId).OrderByDescending(t => t.SubmittedAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var suggestions = await p.Db.MatchSuggestions.AsNoTracking().Where(m => m.PatientId == p.PatientId && !m.Dismissed).OrderBy(m => m.Rank).ToListAsync(ct).ConfigureAwait(false);
        if (triage is null || triage.Status is not (TriageStatus.Reviewed or TriageStatus.Matched) || suggestions.Count == 0)
        {
            return new MatchListDto("pending", []);
        }

        var cards = (await directory.ListAsync(p.Language, suggestions.Select(s => s.TherapistUserId), ct).ConfigureAwait(false)).ToDictionary(t => t.Id);
        var best = suggestions.Min(s => s.Rank);
        return new MatchListDto("ready", suggestions.Where(s => cards.ContainsKey(s.TherapistUserId)).Select(s =>
        {
            var t = cards[s.TherapistUserId];
            return new MatchSuggestionDto(t, s.Rank, s.Rank == best, new MatchReasonsDto(
                s.SharedSpecialties.Split(',', StringSplitOptions.RemoveEmptyEntries), t.Languages.Contains(p.Language), t.HasEvening, t.NextAvailableAt));
        }).ToList());
    }

    /// <summary>PB-36: removes the suggestion with no penalty; the optional reason is one of MatchSuggestion.DismissReasons.</summary>
    internal static async Task<Ok<MatchListDto>> DismissAsync(Guid therapistId, DismissRequest? request, PatientContext p, TherapistDirectory directory, IAuditLog audit, CancellationToken ct)
    {
        var suggestion = await p.Db.MatchSuggestions.FirstOrDefaultAsync(m => m.PatientId == p.PatientId && m.TherapistUserId == therapistId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, "Suggestion not found.");
        var reason = string.IsNullOrWhiteSpace(request?.Reason) ? null : request.Reason.Trim();
        suggestion.Dismiss(reason);
        audit.Append("match.dismissed", "MatchSuggestion", suggestion.Id.ToString(), (await p.AccountAsync(ct).ConfigureAwait(false)).PseudonymId, reason is null ? null : $"reason={reason}");
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(await BuildMatchesAsync(p, directory, ct).ConfigureAwait(false));
    }

    // ---------- P24, P08 ----------

    internal static async Task<Ok<List<TherapistSummaryDto>>> DirectoryAsync(HttpContext http, PatientContext p, TherapistDirectory directory, CancellationToken ct)
    {
        var q = http.Request.Query;
        var all = await directory.ListAsync(p.Language, null, ct).ConfigureAwait(false);
        var search = q["q"].ToString().Trim();
        var specialty = q["specialty"].ToString();
        var language = q["language"].ToString();
        var gender = q["gender"].ToString();
        var evening = q["evening"].ToString() == "true";
        var stems = SpecialtyLabels(p.Language);
        var filtered = all.Where(t =>
            (search.Length == 0 || t.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) || t.Title.Contains(search, StringComparison.OrdinalIgnoreCase)
                || t.Specialties.Any(s => stems.TryGetValue(s, out var label) && label.Contains(search, StringComparison.OrdinalIgnoreCase)))
            && (specialty.Length == 0 || t.Specialties.Contains(specialty))
            && (language.Length == 0 || t.Languages.Contains(language))
            && (gender.Length == 0 || t.Gender == gender)
            && (!evening || t.HasEvening)).ToList();
        return TypedResults.Ok(filtered);
    }

    internal static async Task<Ok<List<SlotDto>>> SlotsAsync(Guid id, HttpContext http, PatientContext p, IClock clock, CancellationToken ct)
    {
        await EnsureCanBookAsync(p, ct).ConfigureAwait(false);
        var days = int.TryParse(http.Request.Query["days"], NumberStyles.None, CultureInfo.InvariantCulture, out var d) ? Math.Clamp(d, 1, 30) : 14;
        var now = clock.UtcNow;
        var until = now.AddDays(days);

        // Times at least 2 hours ahead, so the therapist can read the file first.
        var earliest = now + TherapistDirectory.MinimumNotice;
        var slots = await p.Db.AvailabilitySlots.AsNoTracking()
            .Where(s => s.TherapistUserId == id && s.StartsAtUtc > earliest && s.StartsAtUtc <= until
                        && (s.Status == SlotStatus.Open || (s.Status == SlotStatus.Held && (s.HeldUntilUtc <= now || s.HeldByPatientId == p.PatientId))))
            .OrderBy(s => s.StartsAtUtc).Select(s => new SlotDto(s.Id, s.StartsAtUtc)).ToListAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(slots);
    }

    internal static async Task<Ok<HoldDto>> HoldAsync(HoldRequest request, PatientContext p, TherapistDirectory directory, IClock clock, CancellationToken ct)
    {
        await EnsureCanBookAsync(p, ct).ConfigureAwait(false);

        // One hold at a time: choosing another time releases the previous one.
        var previous = await p.Db.AvailabilitySlots.Where(s => s.HeldByPatientId == p.PatientId && s.Status == SlotStatus.Held).ToListAsync(ct).ConfigureAwait(false);
        foreach (var old in previous.Where(s => s.Id != request.SlotId))
        {
            old.Release();
        }

        var slot = await p.Db.AvailabilitySlots.FirstOrDefaultAsync(s => s.Id == request.SlotId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.SlotUnavailable, "Slot not found.");
        if (slot.StartsAtUtc <= clock.UtcNow + TherapistDirectory.MinimumNotice)
        {
            throw new DomainException(ErrorCodes.SlotUnavailable, "Slot too soon.");
        }

        var holdId = slot.Hold(p.PatientId, clock);
        await SaveOrConflictAsync(p.Db, ct).ConfigureAwait(false);
        var therapist = (await directory.ListAsync(p.Language, [slot.TherapistUserId], ct).ConfigureAwait(false)).Single();
        return TypedResults.Ok(new HoldDto(holdId, slot.Id, slot.StartsAtUtc, slot.HeldUntilUtc!.Value, therapist));
    }

    internal static async Task<NoContent> ReleaseAsync(Guid holdId, PatientContext p, WaitlistNotifier waitlist, CancellationToken ct)
    {
        var slot = await p.Db.AvailabilitySlots.FirstOrDefaultAsync(s => s.HoldId == holdId && s.HeldByPatientId == p.PatientId, ct).ConfigureAwait(false);
        if (slot is not null && slot.Status == SlotStatus.Held)
        {
            slot.Release();
            await SaveOrConflictAsync(p.Db, ct).ConfigureAwait(false);
            await waitlist.CheckAsync(ct).ConfigureAwait(false);
        }

        return TypedResults.NoContent();
    }

    internal static async Task<Ok<AppointmentDetailDto>> ConfirmAsync(Guid holdId, PatientContext p, TherapistDirectory directory, ScopeRunner scopes, CareRecords care, IAuditLog audit, INotifier notifier, IOptions<PlatformOptions> platform, IClock clock, CancellationToken ct)
    {
        await EnsureCanBookAsync(p, ct).ConfigureAwait(false);
        var slot = await p.Db.AvailabilitySlots.FirstOrDefaultAsync(s => s.HoldId == holdId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.HoldExpired, $"Hold {holdId} not found.");
        slot.Book(holdId, p.PatientId, clock);
        var previous = await p.Db.Appointments.CountAsync(a => a.PatientId == p.PatientId && a.TherapistUserId == slot.TherapistUserId
            && a.Status != AppointmentStatus.CancelledByPatient && a.Status != AppointmentStatus.CancelledByTherapist, ct).ConfigureAwait(false);
        var appointment = Appointment.Book(p.PatientId, slot, previous + 1, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(), clock);
        p.Db.Appointments.Add(appointment);

        var profile = await p.ProfileAsync(ct).ConfigureAwait(false);
        var triage = await p.Db.TriageCases.Where(t => t.PatientId == p.PatientId).OrderByDescending(t => t.SubmittedAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (triage is null && profile.CarePath == "direct")
        {
            var screening = await p.Db.Screenings.Where(s => s.PatientId == p.PatientId).OrderByDescending(s => s.CompletedAtUtc).FirstAsync(ct).ConfigureAwait(false);
            triage = TriageCase.Direct(p.PatientId, screening.Level, screening.Positive, clock);
            p.Db.TriageCases.Add(triage);
        }

        triage?.MarkMatched();
        audit.Append("appointment.booked", "Appointment", appointment.Id.ToString(), profile.PseudonymId);
        await SaveOrConflictAsync(p.Db, ct).ConfigureAwait(false);

        // Booking a therapist gives them access to the file: the assignment is created by the scheduling service.
        var patientId = p.PatientId;
        var therapistId = slot.TherapistUserId;
        // P44: booking with a new therapist completes a change-of-therapist request; the old assignment ends first
        // (a patient has one active assignment).
        await CareJourneyEndpoints.CompleteTransferAsync(p, scopes, care, therapistId, profile.PseudonymId, clock, ct).ConfigureAwait(false);
        await scopes.AsServiceAsync(SchedulingServiceId, AppRoles.SchedulingService, async sp =>
        {
            var sdb = sp.GetRequiredService<TamaneenDbContext>();
            if (!await sdb.CareAssignments.AnyAsync(a => a.PatientId == patientId && a.TherapistUserId == therapistId && a.EndedAtUtc == null, ct).ConfigureAwait(false))
            {
                sdb.CareAssignments.Add(CareAssignment.Start(patientId, therapistId, clock));
                sp.GetRequiredService<IAuditLog>().Append("assignment.started", "CareAssignment", null, profile.PseudonymId, "booking");
                await sdb.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            return true;
        }).ConfigureAwait(false);

        // S41: while the clinical file waits for the therapist's approval, the notice opens it (/patients/:id/file).
        var filePending = await StaffScheduleEndpoints.FilePendingAsync(p.Db, [patientId], ct).ConfigureAwait(false);
        var staffLink = filePending.GetValueOrDefault(patientId) ? $"/patients/{profile.PseudonymId}/file" : null;
        await NotifyBothAsync(notifier, appointment, profile.PseudonymId, NotificationKinds.AppointmentBooked, NotificationKinds.StaffAppointmentBooked, ct, staffLink).ConfigureAwait(false);
        return TypedResults.Ok(await DetailDtoAsync(p, directory, appointment, platform, clock, ct).ConfigureAwait(false));
    }

    /// <summary>The patient and the therapist hear about a booking, a move or a cancellation (in-app; the patient may get a neutral copy).</summary>
    internal static async Task NotifyBothAsync(INotifier notifier, Appointment a, string pseudonymId, string patientKind, string staffKind, CancellationToken ct, string? staffLink = null)
    {
        var data = new Dictionary<string, string>
        {
            ["appointmentId"] = a.Id.ToString(),
            ["startsAt"] = a.StartsAtUtc.ToString("O", CultureInfo.InvariantCulture),
            ["therapistId"] = a.TherapistUserId.ToString(),
        };
        await notifier.NotifyAsync(new Notification(a.PatientId, patientKind, data), ct).ConfigureAwait(false);
        await notifier.NotifyAsync(new Notification(a.TherapistUserId, staffKind, new Dictionary<string, string>(data) { ["pseudonymId"] = pseudonymId }, staffLink), ct).ConfigureAwait(false);
    }

    // ---------- P27, P28, P26 ----------

    internal static async Task<Ok<AppointmentListDto>> AppointmentsAsync(PatientContext p, TherapistDirectory directory, IClock clock, CancellationToken ct)
    {
        var all = await p.Db.Appointments.AsNoTracking().Where(a => a.PatientId == p.PatientId).OrderBy(a => a.StartsAtUtc).ToListAsync(ct).ConfigureAwait(false);
        var cards = (await directory.ListAsync(p.Language, all.Select(a => a.TherapistUserId).Distinct(), ct).ConfigureAwait(false)).ToDictionary(t => t.Id);
        var now = clock.UtcNow;
        AppointmentDto Map(Appointment a) => new(a.Id, cards[a.TherapistUserId], a.StartsAtUtc, a.DurationMinutes, a.SequenceNumber, StatusCode(a.Status), a.Rescheduled,
            a.TherapistLateMinutes, a.CancelledBy, PatientCancelReason(a), a.Missed);
        var upcoming = all.Where(a => a.Status == AppointmentStatus.Confirmed && a.StartsAtUtc.AddMinutes(a.DurationMinutes) > now).Select(Map).ToList();
        var past = all.Where(a => !(a.Status == AppointmentStatus.Confirmed && a.StartsAtUtc.AddMinutes(a.DurationMinutes) > now)).OrderByDescending(a => a.StartsAtUtc).Select(Map).ToList();
        return TypedResults.Ok(new AppointmentListDto(upcoming, past));
    }

    internal static async Task<Ok<AppointmentDetailDto>> DetailAsync(Guid id, PatientContext p, TherapistDirectory directory, IOptions<PlatformOptions> platform, IClock clock, CancellationToken ct)
    {
        var appointment = await FindAsync(p, id, ct).ConfigureAwait(false);
        return TypedResults.Ok(await DetailDtoAsync(p, directory, appointment, platform, clock, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// A neutral calendar entry (PB-42): the title is just «موعد» / "Appointment", and neither the title, the file name,
    /// the UID nor PRODID names the platform, the therapist or the patient, so a shared calendar reveals nothing.
    /// </summary>
    internal static async Task<IResult> CalendarAsync(Guid id, PatientContext p, IStringLocalizer<SharedResource> text, CancellationToken ct)
    {
        var a = await FindAsync(p, id, ct).ConfigureAwait(false);
        var summary = LocalizationSetup.WithLanguage(p.Language, () => text["calendar.summary"].Value);
        var description = LocalizationSetup.WithLanguage(p.Language, () => text["calendar.description"].Value);
        static string Stamp(DateTimeOffset t) => t.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        static string Escape(string s) => s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(";", "\\;", StringComparison.Ordinal).Replace(",", "\\,", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
        var ics = new StringBuilder()
            .Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Calendar//Appointment//EN\r\nCALSCALE:GREGORIAN\r\nMETHOD:PUBLISH\r\n")
            .Append("BEGIN:VEVENT\r\n")
            .Append(CultureInfo.InvariantCulture, $"UID:{a.Id:N}\r\n")
            .Append(CultureInfo.InvariantCulture, $"DTSTAMP:{Stamp(a.CreatedAtUtc)}\r\n")
            .Append(CultureInfo.InvariantCulture, $"DTSTART:{Stamp(a.StartsAtUtc)}\r\n")
            .Append(CultureInfo.InvariantCulture, $"DTEND:{Stamp(a.StartsAtUtc.AddMinutes(a.DurationMinutes))}\r\n")
            .Append(CultureInfo.InvariantCulture, $"SUMMARY:{Escape(summary)}\r\n")
            .Append(CultureInfo.InvariantCulture, $"DESCRIPTION:{Escape(description)}\r\n")
            .Append("BEGIN:VALARM\r\nTRIGGER:-PT1H\r\nACTION:DISPLAY\r\n")
            .Append(CultureInfo.InvariantCulture, $"DESCRIPTION:{Escape(summary)}\r\n")
            .Append("END:VALARM\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n")
            .ToString();
        return Results.File(Encoding.UTF8.GetBytes(ics), "text/calendar; charset=utf-8", "appointment.ics");
    }

    /// <summary>P28 cancel (PB-45): the patient picks a reason code from <see cref="CancelReasons.Patient"/>.</summary>
    internal static async Task<Ok<AppointmentDetailDto>> CancelAsync(Guid id, CancelRequest? request, PatientContext p, TherapistDirectory directory, WaitlistNotifier waitlist, IAuditLog audit, INotifier notifier, IOptions<PlatformOptions> platform, IClock clock, CancellationToken ct)
    {
        var reason = request?.ReasonCode?.Trim();
        if (string.IsNullOrEmpty(reason) || !CancelReasons.Patient.Contains(reason))
        {
            throw new DomainException(ErrorCodes.Validation, "Cancel reason missing or unknown.")
                .With("errors", new Dictionary<string, string[]> { ["reasonCode"] = [string.IsNullOrEmpty(reason) ? "required" : "invalid"] });
        }

        var appointment = await FindAsync(p, id, ct).ConfigureAwait(false);
        appointment.CancelByPatient(reason, clock);
        var slot = await p.Db.AvailabilitySlots.FirstAsync(s => s.Id == appointment.SlotId, ct).ConfigureAwait(false);
        slot.Free();
        var pseudonym = (await p.AccountAsync(ct).ConfigureAwait(false)).PseudonymId;
        audit.Append("appointment.cancelled", "Appointment", appointment.Id.ToString(), pseudonym, $"patient:{reason}");
        await SaveOrConflictAsync(p.Db, ct).ConfigureAwait(false);
        await NotifyBothAsync(notifier, appointment, pseudonym, NotificationKinds.AppointmentCancelled, NotificationKinds.StaffAppointmentCancelled, ct).ConfigureAwait(false);

        // P56: the freed time may be what someone on the waitlist is waiting for.
        await waitlist.CheckAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(await DetailDtoAsync(p, directory, appointment, platform, clock, ct).ConfigureAwait(false));
    }

    internal static async Task<Ok<AppointmentDetailDto>> RescheduleAsync(Guid id, RescheduleRequest request, PatientContext p, TherapistDirectory directory, WaitlistNotifier waitlist, SessionRoom room, IAuditLog audit, INotifier notifier, IOptions<PlatformOptions> platform, IClock clock, CancellationToken ct)
    {
        var appointment = await FindAsync(p, id, ct).ConfigureAwait(false);
        var target = await p.Db.AvailabilitySlots.FirstOrDefaultAsync(s => s.Id == request.SlotId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.SlotUnavailable, "Slot not found.");
        var old = await p.Db.AvailabilitySlots.FirstAsync(s => s.Id == appointment.SlotId, ct).ConfigureAwait(false);

        // P54: when the therapist is late, moving the session is free and not bound by the 24-hour limit.
        var therapistLate = !appointment.CanChange(clock)
            && Tamaneen.Clinical.Sessions.SessionWindow.TherapistLate(appointment, clock.UtcNow, await room.TherapistJoinedAtAsync(appointment.Id, ct).ConfigureAwait(false));
        target.TakeFor(p.PatientId, clock);
        if (therapistLate)
        {
            appointment.MoveAfterTherapistLate(target, clock);
        }
        else
        {
            appointment.MoveTo(target, clock);
        }

        old.Free();
        var pseudonym = (await p.AccountAsync(ct).ConfigureAwait(false)).PseudonymId;
        audit.Append("appointment.rescheduled", "Appointment", appointment.Id.ToString(), pseudonym, therapistLate ? "therapist_late" : null);
        await SaveOrConflictAsync(p.Db, ct).ConfigureAwait(false);
        await NotifyBothAsync(notifier, appointment, pseudonym, NotificationKinds.AppointmentRescheduled, NotificationKinds.StaffAppointmentRescheduled, ct).ConfigureAwait(false);
        await waitlist.CheckAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(await DetailDtoAsync(p, directory, appointment, platform, clock, ct).ConfigureAwait(false));
    }

    // ---------- helpers ----------

    /// <summary>Booking needs a passed safety check (direct path) or a reviewed file (intake path), and no crisis lock.</summary>
    internal static async Task EnsureCanBookAsync(PatientContext p, CancellationToken ct)
    {
        await p.EnsureNotLockedAsync(ct).ConfigureAwait(false);
        var profile = await p.ProfileAsync(ct).ConfigureAwait(false);
        // Escalated or closed-with-referral cases do not open booking.
        var reviewed = await p.Db.TriageCases.AnyAsync(t => t.PatientId == p.PatientId && (t.Status == TriageStatus.Reviewed || t.Status == TriageStatus.Matched), ct).ConfigureAwait(false);
        var screened = profile.CarePath == "direct" && await p.Db.Screenings.AnyAsync(s => s.PatientId == p.PatientId, ct).ConfigureAwait(false);
        var hasBooked = await p.Db.Appointments.AnyAsync(a => a.PatientId == p.PatientId, ct).ConfigureAwait(false);
        if (!reviewed && !screened && !hasBooked)
        {
            throw new DomainException(ErrorCodes.BookingNotEligible, "Booking not allowed yet.");
        }
    }

    internal static string StatusCode(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Confirmed => "confirmed",
        AppointmentStatus.CancelledByPatient => "cancelled_by_patient",
        AppointmentStatus.CancelledByTherapist => "cancelled_by_therapist",
        AppointmentStatus.Completed => "completed",
        _ => "no_show",
    };

    internal static async Task<AppointmentDetailDto> DetailDtoAsync(PatientContext p, TherapistDirectory directory, Appointment a, IOptions<PlatformOptions> platform, IClock clock, CancellationToken ct)
    {
        var therapist = (await directory.ListAsync(p.Language, [a.TherapistUserId], ct).ConfigureAwait(false)).Single();
        var join = a.JoinOpen(clock) ? $"{platform.Value.VideoBaseUrl.TrimEnd('/')}/{a.RoomId}" : null;
        return new AppointmentDetailDto(a.Id, therapist, a.StartsAtUtc, a.DurationMinutes, a.SequenceNumber, StatusCode(a.Status), a.Rescheduled, a.CanChange(clock), a.ChangeDeadlineUtc, join, a.StartsAtUtc - Appointment.JoinOpensBefore,
            a.TherapistLateMinutes, a.CancelledBy, PatientCancelReason(a), a.Missed);
    }

    /// <summary>The patient sees their own cancel reason; a therapist's real reason is never shown to them (P67).</summary>
    internal static string? PatientCancelReason(Appointment a) => a.Status == AppointmentStatus.CancelledByPatient ? a.CancelReason : null;

    private static async Task<Appointment> FindAsync(PatientContext p, Guid id, CancellationToken ct) =>
        await p.Db.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.PatientId == p.PatientId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Appointment {id} not found.");

    /// <summary>Two patients racing for one slot: the loser gets the localized "no longer available".</summary>
    internal static async Task SaveOrConflictAsync(TamaneenDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException(ErrorCodes.SlotUnavailable, "Slot changed concurrently.");
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UX_Appointments_OneConfirmedPerSlot", StringComparison.Ordinal) == true)
        {
            throw new DomainException(ErrorCodes.SlotUnavailable, "Slot already booked.");
        }
    }

    private static Dictionary<string, string> SpecialtyLabels(string language) => language == "en"
        ? new() { ["anxiety"] = "anxiety", ["stress"] = "stress", ["sleep"] = "sleep", ["depression"] = "depression", ["cbt"] = "cbt", ["mindfulness"] = "mindfulness", ["work_stress"] = "work stress", ["adjustment"] = "adjustment", ["social_anxiety"] = "social anxiety", ["trauma"] = "trauma" }
        : new() { ["anxiety"] = "القلق", ["stress"] = "الضغط", ["sleep"] = "النوم", ["depression"] = "الاكتئاب", ["cbt"] = "المعرفي السلوكي", ["mindfulness"] = "اليقظة", ["work_stress"] = "الضغط المهني", ["adjustment"] = "التكيف", ["social_anxiety"] = "القلق الاجتماعي", ["trauma"] = "الصدمات" };
}
