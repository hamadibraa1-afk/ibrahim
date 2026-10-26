using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Tamaneen.Api.Clinical;
using Tamaneen.Api.Patients;
using Tamaneen.Clinical.Care;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Crisis;
using Tamaneen.Hosting;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Security;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Endpoints;

/// <summary>
/// The patient's care record in the staff portal: overview (S14), treatment plan (S15), homework (S16), session notes
/// (S33), safety-plan builder (S35) and end of care (S57). Queries run as the signed-in clinician (row-level
/// security): a therapist reaches only patients with an active assignment, anyone else gets 404. Every read is audited.
/// Writes are the treating therapist's; supervisors and the clinical lead read, and sign off closures.
/// </summary>
public static partial class StaffCareEndpoints
{
    public sealed record OverviewAppointmentDto(Guid Id, DateTimeOffset StartsAt, int DurationMinutes, int SequenceNumber, string Status, string NoteStatus);

    public sealed record MoodPointDto(DateTimeOffset At, int Score);

    public sealed record OverviewHomeworkDto(Guid Id, string Title, string Frequency, int DoneThisWeek, int WeeklyTarget, double? AverageFeeling);

    public sealed record OverviewPlanDto(int Version, DateTimeOffset CreatedAt, DateOnly ReviewOn, IReadOnlyList<PlanGoalDto> Goals);

    public sealed record OverviewSafetyPlanDto(bool Exists, int Version, DateTimeOffset? UpdatedAt, string? UpdatedByRole);

    public sealed record OverviewAlertDto(Guid EventId, int Level, string Status, string Source, DateTimeOffset OpenedAt);

    public sealed record PatientOverviewDto(
        string PseudonymId,
        int RiskLevel,
        int CrisisFloor,
        DateTimeOffset? CareStartedAt,
        bool InCare,
        string? ClosureStatus,
        bool FileApproved,
        int? Phq9AtIntake,
        int? Gad7AtIntake,
        OverviewAppointmentDto? NextAppointment,
        IReadOnlyList<OverviewAppointmentDto> PastAppointments,
        IReadOnlyList<MoodPointDto> Mood,
        double? MoodAverage7Days,
        OverviewPlanDto? Plan,
        IReadOnlyList<OverviewHomeworkDto> Homework,
        OverviewSafetyPlanDto SafetyPlan,
        IReadOnlyList<OverviewAlertDto> Alerts,
        bool CanEdit);

    public static IEndpointRouteBuilder MapStaffCareEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/staff").RequireAuthorization(AuthSetup.ClinicalPolicy);
        g.MapGet("/patients/{pseudonymId}/overview", OverviewAsync);

        g.MapGet("/homework/templates", HomeworkTemplatesAsync);
        g.MapGet("/patients/{pseudonymId}/homework", HomeworkAsync);
        g.MapPost("/patients/{pseudonymId}/homework", AssignHomeworkAsync);
        g.MapPut("/homework/{id:guid}", UpdateHomeworkAsync);
        g.MapDelete("/homework/{id:guid}", CancelHomeworkAsync);
        g.MapPost("/checkins/{id:guid}/feedback", FeedbackAsync);

        g.MapGet("/patients/{pseudonymId}/plans", PlansAsync);
        g.MapGet("/patients/{pseudonymId}/plans/{version:int}", PlanVersionAsync);
        g.MapPost("/patients/{pseudonymId}/plans", SavePlanAsync);

        g.MapGet("/patients/{pseudonymId}/sessions/{appointmentId:guid}/note", NoteAsync);
        g.MapPut("/patients/{pseudonymId}/sessions/{appointmentId:guid}/note", SaveNoteAsync);
        g.MapPost("/patients/{pseudonymId}/sessions/{appointmentId:guid}/note/sign", SignNoteAsync);
        g.MapPost("/patients/{pseudonymId}/sessions/{appointmentId:guid}/note/addenda", AddAddendumAsync);

        g.MapGet("/patients/{pseudonymId}/safety-plan", SafetyPlanAsync);
        g.MapGet("/patients/{pseudonymId}/safety-plan/versions", SafetyPlanVersionsAsync);
        g.MapGet("/patients/{pseudonymId}/safety-plan/versions/{version:int}", SafetyPlanVersionAsync);
        g.MapPost("/patients/{pseudonymId}/safety-plans", SaveSafetyPlanAsync);

        g.MapGet("/patients/{pseudonymId}/closure", ClosureAsync);
        g.MapPut("/patients/{pseudonymId}/closure", SaveClosureAsync);
        g.MapPost("/patients/{pseudonymId}/closure/withdraw", WithdrawClosureAsync);

        var supervisors = app.MapGroup("/v1/staff/closures").RequireAuthorization(AuthSetup.CrisisCloserPolicy);
        supervisors.MapGet("", ClosureQueueAsync);
        supervisors.MapPost("/{id:guid}/sign-off", SignOffAsync);

        var patient = app.MapGroup("/v1").RequireAuthorization(AuthSetup.PatientPolicy);
        patient.MapGet("/appointments/{id:guid}/session-summary", SessionSummaryAsync);
        return app;
    }

    /// <summary>S14: risk, sessions, mood trend, plan goals, this week's homework, safety plan and alerts at a glance. Audited.</summary>
    internal static async Task<Ok<PatientOverviewDto>> OverviewAsync(string pseudonymId, ICurrentUser user, CareRecords care, IPatientDataProtector protector, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var db = care.Db;
        var patientId = await StaffEndpoints.PatientIdAsync(db, pseudonymId, ct).ConfigureAwait(false);
        var (risk, floor) = await care.RiskAsync(patientId, clock, ct).ConfigureAwait(false);
        var triage = await care.ClinicalTriageAsync(patientId, ct).ConfigureAwait(false);
        var now = clock.UtcNow;
        var assignments = await db.CareAssignments.AsNoTracking().Where(a => a.PatientId == patientId).ToListAsync(ct).ConfigureAwait(false);
        var active = assignments.Where(a => a.IsActive).ToList();
        var since = active.Count > 0 ? active.Min(a => a.StartedAtUtc) : assignments.Count > 0 ? assignments.Min(a => a.StartedAtUtc) : (DateTimeOffset?)null;

        var appointments = await db.Appointments.AsNoTracking().Where(a => a.PatientId == patientId).OrderBy(a => a.StartsAtUtc).ToListAsync(ct).ConfigureAwait(false);
        var appointmentIds = appointments.Select(a => a.Id).ToList();
        var notes = await db.SessionNotes.AsNoTracking().Where(n => appointmentIds.Contains(n.AppointmentId)).ToDictionaryAsync(n => n.AppointmentId, n => n.Status, ct).ConfigureAwait(false);
        OverviewAppointmentDto Map(Appointment a) => new(a.Id, a.StartsAtUtc, a.DurationMinutes, a.SequenceNumber, SchedulingEndpoints.StatusCode(a.Status),
            notes.TryGetValue(a.Id, out var s) ? (s == SessionNoteStatus.Locked ? "signed" : "draft") : "none");
        var next = appointments.FirstOrDefault(a => a.Status == AppointmentStatus.Confirmed && a.StartsAtUtc.AddMinutes(a.DurationMinutes) > now);
        var past = appointments.Where(a => a.StartsAtUtc <= now && a.Id != next?.Id).OrderByDescending(a => a.StartsAtUtc).Take(6).Select(Map).ToList();

        var moodFrom = now.AddDays(-56);
        var mood = await db.MoodEntries.AsNoTracking().Where(m => m.PatientId == patientId && m.RecordedAtUtc >= moodFrom).OrderBy(m => m.RecordedAtUtc).Select(m => new MoodPointDto(m.RecordedAtUtc, m.Score)).ToListAsync(ct).ConfigureAwait(false);
        var week = mood.Where(m => m.At >= now.AddDays(-7)).ToList();

        var planRow = await db.TreatmentPlanVersions.AsNoTracking().Where(v => v.PatientId == patientId).OrderByDescending(v => v.Version).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        OverviewPlanDto? plan = null;
        if (planRow is not null)
        {
            var stored = JsonSerializer.Deserialize<StoredPlan>(await protector.DecryptAsync(patientId, Fields.TreatmentPlan, planRow.ContentCipher, ct).ConfigureAwait(false), PatientContext.Json);
            plan = new OverviewPlanDto(planRow.Version, planRow.CreatedAtUtc, planRow.ReviewOn, stored?.Goals ?? []);
        }

        var today = await care.TodayAsync(patientId, clock, ct).ConfigureAwait(false);
        var homework = (await HomeworkRowsAsync(care, protector, patientId, today, null, ct).ConfigureAwait(false))
            .Where(h => h.Status == "active")
            .Select(h => new OverviewHomeworkDto(h.Id, h.Title, h.Frequency, h.DoneThisWeek, h.WeeklyTarget, h.AverageFeeling)).ToList();

        var safety = await db.SafetyPlans.AsNoTracking().FirstOrDefaultAsync(s => s.PatientId == patientId, ct).ConfigureAwait(false);
        var alertsSince = now.AddDays(-30);
        var alerts = await db.CrisisEvents.AsNoTracking().Where(e => e.PatientId == patientId && (e.Status != CrisisEventStatus.Closed || e.OpenedAtUtc >= alertsSince))
            .OrderByDescending(e => e.OpenedAtUtc).Take(10)
            .Select(e => new { e.Id, e.Level, e.Status, e.Source, e.OpenedAtUtc }).ToListAsync(ct).ConfigureAwait(false);
        var closure = await db.CareClosures.AsNoTracking().Where(c => c.PatientId == patientId).OrderByDescending(c => c.CreatedAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false);

        audit.Append("patient.overview_opened", "UserAccount", patientId.ToString(), StaffEndpoints.Canonical(pseudonymId));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new PatientOverviewDto(
            StaffEndpoints.Canonical(pseudonymId),
            (int)risk,
            (int)floor,
            since,
            active.Count > 0,
            closure is null ? null : ClosureStatusCode(closure.Status),
            await care.FileApprovedAsync(patientId, ct).ConfigureAwait(false),
            triage?.Phq9,
            triage?.Gad7,
            next is null ? null : Map(next),
            past,
            mood,
            week.Count == 0 ? null : Math.Round(week.Average(m => m.Score), 1),
            plan,
            homework,
            new OverviewSafetyPlanDto(safety is not null, safety?.Version ?? 0, safety?.UpdatedAtUtc, safety is null ? null : safety.UpdatedByRole == AppRoles.Patient ? "patient" : StaffEndpoints.RoleCode(safety.UpdatedByRole)),
            alerts.Select(e => new OverviewAlertDto(e.Id, (int)e.Level, e.Status.ToString(), e.Source, e.OpenedAtUtc)).ToList(),
            user.Role == AppRoles.Therapist && active.Any(a => a.TherapistUserId == user.UserId)));
    }
}
