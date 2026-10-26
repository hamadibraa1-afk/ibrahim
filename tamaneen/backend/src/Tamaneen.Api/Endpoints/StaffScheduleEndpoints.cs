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

    public const int MaxNoteLength = 500;

    public static IEndpointRouteBuilder MapStaffScheduleEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/staff").RequireAuthorization(p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Therapist));
        g.MapGet("/schedule", ScheduleAsync);
        g.MapGet("/appointments/{id:guid}", DetailAsync);
        g.MapPost("/appointments/{id:guid}/running-late", RunningLateAsync);
        g.MapPost("/appointments/{id:guid}/cancel", CancelAsync);
        return app;
    }

    /// <summary>?from=yyyy-MM-dd (local, default today) and days=1 or 7 (default 7).</summary>
    internal static async Task<Ok<ScheduleDto>> ScheduleAsync(HttpContext http, ICurrentUser user, TamaneenDbContext db, IAuditLog audit, IOptions<PlatformOptions> platform, IClock clock, CancellationToken ct)
    {
        var zone = AvailabilityEndpoints.Zone(platform.Value);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, zone).DateTime);
        var from = DateOnly.TryParseExact(http.Request.Query["from"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : today;
        var days = int.TryParse(http.Request.Query["days"], NumberStyles.None, CultureInfo.InvariantCulture, out var d) ? Math.Clamp(d, 1, 14) : 7;
        var start = CareEndpoints.DayStartUtc(from, platform.Value.TimeZone);
        var end = CareEndpoints.DayStartUtc(from.AddDays(days), platform.Value.TimeZone);
        var me = user.UserId!.Value;
        var list = await db.Appointments.AsNoTracking().Where(a => a.TherapistUserId == me && a.StartsAtUtc >= start && a.StartsAtUtc < end)
            .OrderBy(a => a.StartsAtUtc).ToListAsync(ct).ConfigureAwait(false);
        var items = await MapAsync(db, list, clock, ct).ConfigureAwait(false);
        audit.Append("schedule.viewed", "Appointment", null, null, $"{from:yyyy-MM-dd}+{days}");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new ScheduleDto(from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), from.AddDays(days - 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), platform.Value.TimeZone, items));
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

    internal static async Task<List<StaffAppointmentDto>> MapAsync(TamaneenDbContext db, IReadOnlyList<Appointment> list, IClock clock, CancellationToken ct)
    {
        var patients = list.Select(a => a.PatientId).Distinct().ToList();
        var pseudonyms = await db.UserAccounts.AsNoTracking().Where(a => patients.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.PseudonymId, ct).ConfigureAwait(false);
        var risk = await RiskAsync(db, patients, clock, ct).ConfigureAwait(false);

        // The file waits for approval: a submitted intake not yet approved, or a direct booking whose triage is unreviewed.
        var intakes = (await db.IntakeSessions.AsNoTracking().Where(i => patients.Contains(i.PatientId) && i.Status == IntakeStatus.Submitted)
            .Select(i => new { i.PatientId, i.SubmittedAtUtc, i.TherapistApprovedAtUtc }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(i => i.PatientId).ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.SubmittedAtUtc).First().TherapistApprovedAtUtc is null);
        var direct = (await db.TriageCases.AsNoTracking().Where(t => patients.Contains(t.PatientId))
            .Select(t => new { t.PatientId, t.Path, t.ReviewedAtUtc, t.SubmittedAtUtc }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(t => t.PatientId).ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.SubmittedAtUtc).First() is var t && t.Path == "direct" && t.ReviewedAtUtc is null);
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
                intakes.TryGetValue(a.PatientId, out var pending) ? pending : direct.GetValueOrDefault(a.PatientId),
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
