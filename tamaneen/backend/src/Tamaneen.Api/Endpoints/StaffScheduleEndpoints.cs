using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamaneen.Api.Clinical;
using Tamaneen.Api.Patients;
using Tamaneen.Clinical.Intake;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Hosting;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.Infrastructure.Security;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Endpoints;

/// <summary>
/// S18 schedule: the therapist's sessions by day or week, and what can be done to one (SB-25, SB-26): record the
/// outcome (existing endpoint), say you are running late, or cancel with a reason. Row-level security already
/// limits a therapist to their own appointments; the queries filter on the therapist as well.
/// </summary>
public static class StaffScheduleEndpoints
{
    public sealed record StaffAppointmentDto(
        Guid Id,
        string PseudonymId,
        DateTimeOffset StartsAt,
        int DurationMinutes,
        int SequenceNumber,
        string Status,
        int RiskLevel,
        bool FilePending,
        bool OutcomeRecorded,
        bool CanRecordOutcome,
        bool CanCancel,
        bool CanReportLate,
        int? TherapistLateMinutes,
        string? CancelledBy,
        string? CancelReasonCode,
        bool Missed);

    public sealed record ScheduleDto(string From, string To, string TimeZone, IReadOnlyList<StaffAppointmentDto> Items);

    public sealed record RunningLateRequest(int? Minutes);

    public sealed record TherapistCancelRequest(string? ReasonCode, string? Note);

    public sealed record TherapistRescheduleRequest(Guid? SlotId, string? ReasonCode);

    public sealed record BookNextRequest(Guid? SlotId);

    public sealed record FreeSlotDto(Guid SlotId, DateTimeOffset StartsAt, int DurationMinutes);

    public const int MaxNoteLength = 500;

    public static IEndpointRouteBuilder MapStaffScheduleEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/staff").RequireAuthorization(p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Therapist));
        g.MapGet("/schedule", ScheduleAsync);
        g.MapGet("/appointments/{id:guid}", DetailAsync);
        g.MapPost("/appointments/{id:guid}/running-late", RunningLateAsync);
        g.MapPost("/appointments/{id:guid}/cancel", CancelAsync);
        g.MapPost("/appointments/{id:guid}/reschedule", RescheduleAsync);
        g.MapGet("/free-slots", FreeSlotsAsync);
        g.MapPost("/patients/{pseudonymId}/appointments", BookNextAsync);
        return app;
    }

    /// <summary>?from=yyyy-MM-dd (local, default today) and days=1 or 7 (default 7).</summary>
    internal static async Task<Ok<ScheduleDto>> ScheduleAsync(HttpContext http, ICurrentUser user, TamaneenDbContext db, IAuditLog audit, IOptions<PlatformOptions> platform, IClock clock, CancellationToken ct)
    {
        var me = user.UserId!.Value;

        // S32: days run in the time zone the therapist chose (the platform's by default).
        var zoneId = await db.StaffProfiles.AsNoTracking().Where(p => p.UserId == me).Select(p => p.TimeZone).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? platform.Value.TimeZone;
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var zone))
        {
            zoneId = platform.Value.TimeZone;
            zone = AvailabilityEndpoints.Zone(platform.Value);
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, zone).DateTime);
        var from = DateOnly.TryParseExact(http.Request.Query["from"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : today;
        var days = int.TryParse(http.Request.Query["days"], NumberStyles.None, CultureInfo.InvariantCulture, out var d) ? Math.Clamp(d, 1, 14) : 7;
        var start = CareEndpoints.DayStartUtc(from, zoneId);
        var end = CareEndpoints.DayStartUtc(from.AddDays(days), zoneId);
        var list = await db.Appointments.AsNoTracking().Where(a => a.TherapistUserId == me && a.StartsAtUtc >= start && a.StartsAtUtc < end)
            .OrderBy(a => a.StartsAtUtc).ToListAsync(ct).ConfigureAwait(false);
        var items = await MapAsync(db, list, clock, ct).ConfigureAwait(false);
        audit.Append("schedule.viewed", "Appointment", null, null, $"{from:yyyy-MM-dd}+{days}");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new ScheduleDto(from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), from.AddDays(days - 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), zoneId, items));
    }

    internal static async Task<Ok<StaffAppointmentDto>> DetailAsync(Guid id, ICurrentUser user, TamaneenDbContext db, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var a = await FindAsync(db, user, id, ct).ConfigureAwait(false);
        var dto = (await MapAsync(db, [a], clock, ct).ConfigureAwait(false)).Single();
        audit.Append("appointment.viewed", "Appointment", a.Id.ToString(), dto.PseudonymId);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(dto);
    }

    internal static async Task<Ok<StaffAppointmentDto>> RunningLateAsync(Guid id, RunningLateRequest request, ICurrentUser user, TamaneenDbContext db, AppointmentNotices notices, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (request.Minutes is not { } minutes || minutes < 1 || minutes > Appointment.MaxLateMinutes)
        {
            throw MeEndpoints.Invalid("minutes");
        }

        var a = await FindAsync(db, user, id, ct).ConfigureAwait(false);
        a.ReportLate(minutes, clock);
        var pseudonym = await PseudonymAsync(db, a.PatientId, ct).ConfigureAwait(false);
        audit.Append("appointment.running_late", "Appointment", a.Id.ToString(), pseudonym, $"minutes={minutes}");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await notices.LateAsync(a, ct).ConfigureAwait(false);
        return TypedResults.Ok((await MapAsync(db, [a], clock, ct).ConfigureAwait(false)).Single());
    }

    /// <summary>
    /// SB-26 cancel: a reason code is required, the note is optional, encrypted with the patient's key and never shown
    /// to the patient. The time is taken out of the calendar (the therapist can't make it; S19 can reopen it), so it is
    /// not offered to anyone else; the patient lands on P67 to rebook.
    /// </summary>
    internal static async Task<Ok<StaffAppointmentDto>> CancelAsync(Guid id, TherapistCancelRequest request, ICurrentUser user, TamaneenDbContext db, IPatientDataProtector protector, AppointmentNotices notices, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var reason = request.ReasonCode?.Trim();
        if (string.IsNullOrEmpty(reason) || !CancelReasons.Therapist.Contains(reason))
        {
            throw new DomainException(ErrorCodes.Validation, "Cancel reason missing or unknown.")
                .With("errors", new Dictionary<string, string[]> { ["reasonCode"] = [string.IsNullOrEmpty(reason) ? "required" : "invalid"] });
        }

        var note = request.Note?.Trim();
        if (note is { Length: > MaxNoteLength })
        {
            throw MeEndpoints.Invalid("note");
        }

        var a = await FindAsync(db, user, id, ct).ConfigureAwait(false);
        var cipher = string.IsNullOrEmpty(note) ? null : await protector.EncryptAsync(a.PatientId, Fields.TherapistCancelNote, note, ct).ConfigureAwait(false);
        a.CancelByTherapist(reason, cipher, clock);
        var slot = await db.AvailabilitySlots.FirstAsync(s => s.Id == a.SlotId, ct).ConfigureAwait(false);
        slot.CloseAfterTherapistCancel();
        var pseudonym = await PseudonymAsync(db, a.PatientId, ct).ConfigureAwait(false);
        audit.Append("appointment.cancelled", "Appointment", a.Id.ToString(), pseudonym, $"therapist:{reason}");
        await SchedulingEndpoints.SaveOrConflictAsync(db, ct).ConfigureAwait(false);

        await notices.CancelledAsync(a, pseudonym, ct).ConfigureAwait(false);
        return TypedResults.Ok((await MapAsync(db, [a], clock, ct).ConfigureAwait(false)).Single());
    }

    /// <summary>The therapist's own free times from now on (?days=1..60, default 28): for S18 reschedule and S33 "book the next session".</summary>
    internal static async Task<Ok<List<FreeSlotDto>>> FreeSlotsAsync(HttpContext http, ICurrentUser user, TamaneenDbContext db, IClock clock, CancellationToken ct)
    {
        var days = int.TryParse(http.Request.Query["days"], NumberStyles.None, CultureInfo.InvariantCulture, out var d) ? Math.Clamp(d, 1, 60) : 28;
        var now = clock.UtcNow;
        var until = now.AddDays(days);
        var me = user.UserId!.Value;
        var slots = await db.AvailabilitySlots.AsNoTracking()
            .Where(s => s.TherapistUserId == me && s.StartsAtUtc > now && s.StartsAtUtc <= until
                        && (s.Status == SlotStatus.Open || (s.Status == SlotStatus.Held && s.HeldUntilUtc <= now)))
            .OrderBy(s => s.StartsAtUtc).Select(s => new FreeSlotDto(s.Id, s.StartsAtUtc, s.DurationMinutes)).Take(200).ToListAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(slots);
    }

    /// <summary>
    /// SB-26 reschedule by the therapist: a reason code is required and the new time must be one of the therapist's
    /// own free times. The old time is taken out of the calendar, as on a cancel (the therapist said they cannot make
    /// it; S19 can reopen it). The patient is told; the move is audited.
    /// </summary>
    internal static async Task<Ok<StaffAppointmentDto>> RescheduleAsync(Guid id, TherapistRescheduleRequest request, ICurrentUser user, TamaneenDbContext db, AppointmentNotices notices, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var reason = request.ReasonCode?.Trim();
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(reason) || !CancelReasons.Therapist.Contains(reason))
        {
            errors["reasonCode"] = [string.IsNullOrEmpty(reason) ? "required" : "invalid"];
        }

        if (request.SlotId is null)
        {
            errors["slotId"] = ["required"];
        }

        if (errors.Count > 0)
        {
            throw new DomainException(ErrorCodes.Validation, "Reschedule request invalid.").With("errors", errors);
        }

        var a = await FindAsync(db, user, id, ct).ConfigureAwait(false);
        var target = await db.AvailabilitySlots.FirstOrDefaultAsync(s => s.Id == request.SlotId && s.TherapistUserId == a.TherapistUserId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.SlotUnavailable, "Slot not found.");
        var old = await db.AvailabilitySlots.FirstAsync(s => s.Id == a.SlotId, ct).ConfigureAwait(false);
        var from = a.StartsAtUtc;
        target.TakeFor(a.PatientId, clock);
        a.MoveByTherapist(target, clock);
        old.CloseAfterTherapistCancel();
        var pseudonym = await PseudonymAsync(db, a.PatientId, ct).ConfigureAwait(false);
        audit.Append("appointment.rescheduled", "Appointment", a.Id.ToString(), pseudonym, $"therapist:{reason};from={from:O}");
        await SchedulingEndpoints.SaveOrConflictAsync(db, ct).ConfigureAwait(false);
        await notices.RescheduledAsync(a, ct).ConfigureAwait(false);
        return TypedResults.Ok((await MapAsync(db, [a], clock, ct).ConfigureAwait(false)).Single());
    }

    /// <summary>
    /// S33 «حجز الجلسة القادمة»: the therapist books one of their own free times for a patient they actively care for.
    /// The patient is told (P27 lists it); audited as a booking by the therapist.
    /// </summary>
    internal static async Task<Ok<StaffAppointmentDto>> BookNextAsync(string pseudonymId, BookNextRequest request, ICurrentUser user, TamaneenDbContext db, AppointmentNotices notices, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (request.SlotId is not { } slotId)
        {
            throw MeEndpoints.Invalid("slotId");
        }

        var me = user.UserId!.Value;
        var patientId = await StaffEndpoints.PatientIdAsync(db, pseudonymId, ct).ConfigureAwait(false);
        if (!await db.CareAssignments.AnyAsync(a => a.PatientId == patientId && a.TherapistUserId == me && a.EndedAtUtc == null, ct).ConfigureAwait(false))
        {
            throw new DomainException(ErrorCodes.NotFound, "Not this therapist's patient.");
        }

        var slot = await db.AvailabilitySlots.FirstOrDefaultAsync(s => s.Id == slotId && s.TherapistUserId == me, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.SlotUnavailable, "Slot not found.");
        slot.TakeFor(patientId, clock);
        var previous = await db.Appointments.CountAsync(a => a.PatientId == patientId && a.TherapistUserId == me
            && a.Status != AppointmentStatus.CancelledByPatient && a.Status != AppointmentStatus.CancelledByTherapist, ct).ConfigureAwait(false);
        var appointment = Appointment.Book(patientId, slot, previous + 1, Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(), clock);
        db.Appointments.Add(appointment);
        audit.Append("appointment.booked", "Appointment", appointment.Id.ToString(), StaffEndpoints.Canonical(pseudonymId), "therapist");
        await SchedulingEndpoints.SaveOrConflictAsync(db, ct).ConfigureAwait(false);
        await notices.BookedAsync(appointment, ct).ConfigureAwait(false);
        return TypedResults.Ok((await MapAsync(db, [appointment], clock, ct).ConfigureAwait(false)).Single());
    }

    // ---------- helpers ----------

    private static async Task<Appointment> FindAsync(TamaneenDbContext db, ICurrentUser user, Guid id, CancellationToken ct) =>
        await db.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.TherapistUserId == user.UserId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Appointment {id} not found.");

    private static Task<string> PseudonymAsync(TamaneenDbContext db, Guid patientId, CancellationToken ct) =>
        db.UserAccounts.Where(a => a.Id == patientId).Select(a => a.PseudonymId).FirstAsync(ct);

    /// <summary>The level shown for a patient: the latest triage, raised by an open or recent (30 days) crisis event.</summary>
    internal static async Task<Dictionary<Guid, int>> RiskAsync(TamaneenDbContext db, IReadOnlyCollection<Guid> patients, IClock clock, CancellationToken ct)
    {
        var triage = (await db.TriageCases.AsNoTracking().Where(t => patients.Contains(t.PatientId)).Select(t => new { t.PatientId, t.RiskLevel, t.SubmittedAtUtc }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(t => t.PatientId).ToDictionary(g => g.Key, g => (int)g.OrderByDescending(t => t.SubmittedAtUtc).First().RiskLevel);
        var monthAgo = clock.UtcNow.AddDays(-30);
        var crisis = (await db.CrisisEvents.AsNoTracking().Where(e => patients.Contains(e.PatientId) && (e.Status != Tamaneen.Crisis.CrisisEventStatus.Closed || e.OpenedAtUtc >= monthAgo))
            .Select(e => new { e.PatientId, e.Level }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(e => e.PatientId).ToDictionary(g => g.Key, g => (int)g.Max(e => e.Level));
        return patients.Distinct().ToDictionary(p => p, p => Math.Max(triage.GetValueOrDefault(p, (int)RiskLevel.Routine), crisis.GetValueOrDefault(p)));
    }

    /// <summary>The file waits for approval: a submitted intake not yet approved, or a direct booking whose triage is unreviewed.</summary>
    internal static async Task<Dictionary<Guid, bool>> FilePendingAsync(TamaneenDbContext db, IReadOnlyCollection<Guid> patients, CancellationToken ct)
    {
        var intakes = (await db.IntakeSessions.AsNoTracking().Where(i => patients.Contains(i.PatientId) && i.Status == IntakeStatus.Submitted)
            .Select(i => new { i.PatientId, i.SubmittedAtUtc, i.TherapistApprovedAtUtc }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(i => i.PatientId).ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.SubmittedAtUtc).First().TherapistApprovedAtUtc is null);
        var direct = (await db.TriageCases.AsNoTracking().Where(t => patients.Contains(t.PatientId))
            .Select(t => new { t.PatientId, t.Path, t.ReviewedAtUtc, t.SubmittedAtUtc }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(t => t.PatientId).ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.SubmittedAtUtc).First() is var t && t.Path == "direct" && t.ReviewedAtUtc is null);
        return patients.Distinct().ToDictionary(p => p, p => intakes.TryGetValue(p, out var pending) ? pending : direct.GetValueOrDefault(p));
    }

    internal static async Task<List<StaffAppointmentDto>> MapAsync(TamaneenDbContext db, IReadOnlyList<Appointment> list, IClock clock, CancellationToken ct)
    {
        var patients = list.Select(a => a.PatientId).Distinct().ToList();
        var pseudonyms = await db.UserAccounts.AsNoTracking().Where(a => patients.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.PseudonymId, ct).ConfigureAwait(false);
        var risk = await RiskAsync(db, patients, clock, ct).ConfigureAwait(false);

        var pending = await FilePendingAsync(db, patients, ct).ConfigureAwait(false);
        var now = clock.UtcNow;
        return list.Select(a =>
        {
            var confirmed = a.Status == AppointmentStatus.Confirmed;
            var ends = a.StartsAtUtc.AddMinutes(a.DurationMinutes);
            return new StaffAppointmentDto(
                a.Id,
                pseudonyms.GetValueOrDefault(a.PatientId, string.Empty),
                a.StartsAtUtc,
                a.DurationMinutes,
                a.SequenceNumber,
                SchedulingEndpoints.StatusCode(a.Status),
                risk.GetValueOrDefault(a.PatientId, 1),
                pending.GetValueOrDefault(a.PatientId),
                a.OutcomeRecordedAtUtc is not null,
                confirmed && now >= a.StartsAtUtc - Appointment.JoinOpensBefore,
                confirmed && now < ends,
                confirmed && now >= a.StartsAtUtc - Appointment.JoinOpensBefore && now < ends,
                a.TherapistLateMinutes,
                a.CancelledBy,
                a.Status is AppointmentStatus.CancelledByPatient or AppointmentStatus.CancelledByTherapist ? a.CancelReason : null,
                a.Missed);
        }).ToList();
    }
}
