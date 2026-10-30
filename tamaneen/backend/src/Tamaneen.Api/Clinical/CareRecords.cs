using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tamaneen.Api.Patients;
using Tamaneen.Clinical.Intake;
using Tamaneen.Crisis;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;
using Tamaneen.SharedKernel.Notifications;

namespace Tamaneen.Api.Clinical;

/// <summary>
/// Helpers shared by the phase 5 care screens (S14–S16, S33, S35, S57, P44, P53). Every query runs as the caller,
/// so row-level security decides what a clinician sees; a patient the caller may not see is "not found".
/// </summary>
public sealed partial class CareRecords(TamaneenDbContext db, INotifier notifier, ILogger<CareRecords> log)
{
    public TamaneenDbContext Db => db;

    /// <summary>The newest clinical triage case (never a change-of-therapist case, which only exists for S03).</summary>
    public Task<TriageCase?> ClinicalTriageAsync(Guid patientId, CancellationToken ct) =>
        db.TriageCases.AsNoTracking().Where(t => t.PatientId == patientId && t.Path != TriageCase.ChangePath).OrderByDescending(t => t.SubmittedAtUtc).FirstOrDefaultAsync(ct);

    /// <summary>SB-15: approved by the treating therapist (intake path) or reviewed (direct path).</summary>
    public async Task<bool> FileApprovedAsync(Guid patientId, CancellationToken ct)
    {
        var triage = await ClinicalTriageAsync(patientId, ct).ConfigureAwait(false);
        if (triage is null)
        {
            return false;
        }

        if (triage.IntakeId is { } intakeId)
        {
            return await db.IntakeSessions.AsNoTracking().AnyAsync(i => i.Id == intakeId && i.TherapistApprovedAtUtc != null, ct).ConfigureAwait(false);
        }

        return triage.ReviewedAtUtc is not null;
    }

    /// <summary>Current level: the clinical triage level, never below an open or recent crisis.</summary>
    public async Task<(RiskLevel Level, RiskLevel Floor)> RiskAsync(Guid patientId, IClock clock, CancellationToken ct)
    {
        var triage = await ClinicalTriageAsync(patientId, ct).ConfigureAwait(false);
        var floor = await Endpoints.StaffFileEndpoints.CrisisFloorAsync(db, patientId, clock, ct).ConfigureAwait(false);
        return ((RiskLevel)Math.Max((int)(triage?.RiskLevel ?? RiskLevel.Routine), (int)floor), floor);
    }

    /// <summary>Why closing is refused: the risk (<c>risk</c>).</summary>
    public const string BlockedByRisk = "risk";

    /// <summary>Why closing is refused: the patient asked to continue (<c>patient_continue</c>).</summary>
    public const string BlockedByPatient = "patient_continue";

    /// <summary>
    /// S57 / §10.4: closing is refused while the level is 3–4 now, or a crisis at 3–4 opened in the last 14 days
    /// (<see cref="BlockedByRisk"/>), or while the patient's request to continue care, made during the current care, is
    /// open (<see cref="BlockedByPatient"/>). Null when closing is allowed.
    /// </summary>
    public async Task<string?> ClosureBlockReasonAsync(Guid patientId, IClock clock, CancellationToken ct)
    {
        var triage = await ClinicalTriageAsync(patientId, ct).ConfigureAwait(false);
        var since = clock.UtcNow.AddDays(-14);
        if (triage is { RiskLevel: >= RiskLevel.High }
            || await db.CrisisEvents.AsNoTracking().AnyAsync(e => e.PatientId == patientId && e.Level >= RiskLevel.High && (e.OpenedAtUtc >= since || e.Status != CrisisEventStatus.Closed), ct).ConfigureAwait(false))
        {
            return BlockedByRisk;
        }

        var careStarted = await db.CareAssignments.AsNoTracking().Where(a => a.PatientId == patientId && a.EndedAtUtc == null)
            .Select(a => (DateTimeOffset?)a.StartedAtUtc).MinAsync(ct).ConfigureAwait(false);
        var asked = await db.CareContinueRequests.AsNoTracking()
            .AnyAsync(r => r.PatientId == patientId && r.WithdrawnAtUtc == null && (careStarted == null || r.RequestedAtUtc >= careStarted), ct).ConfigureAwait(false);
        return asked ? BlockedByPatient : null;
    }

    public async Task<bool> ClosureBlockedAsync(Guid patientId, IClock clock, CancellationToken ct) =>
        await ClosureBlockReasonAsync(patientId, clock, ct).ConfigureAwait(false) is not null;

    /// <summary>Therapists with an active assignment to the patient (as far as the caller may see).</summary>
    public Task<List<Guid>> ActiveTherapistsAsync(Guid patientId, CancellationToken ct) =>
        db.CareAssignments.AsNoTracking().Where(a => a.PatientId == patientId && a.EndedAtUtc == null).Select(a => a.TherapistUserId).ToListAsync(ct);

    /// <summary>Writes are the treating therapist's (row-level security already limits a therapist to assigned patients).</summary>
    public static void RequireTherapist(ICurrentUser user)
    {
        if (user.Role != AppRoles.Therapist)
        {
            throw new DomainException(ErrorCodes.NotFound, "Only the treating therapist writes care records.");
        }
    }

    public async Task<string> StaffNameAsync(Guid? userId, string language, CancellationToken ct)
    {
        if (userId is not { } id)
        {
            return string.Empty;
        }

        return await db.StaffProfiles.AsNoTracking().Where(s => s.UserId == id).Select(s => language == "en" ? s.DisplayNameEn : s.DisplayNameAr).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;
    }

    public Task<List<Guid>> SupervisorIdsAsync(CancellationToken ct) =>
        db.UserAccounts.AsNoTracking().Where(a => a.Role == AppRoles.Supervisor || a.Role == AppRoles.ClinicalLead).Select(a => a.Id).ToListAsync(ct);

    /// <summary>
    /// Hands a notification to the notification service. A failure there never undoes the clinical write that
    /// caused it; it is logged (kind only, no content).
    /// </summary>
    public async Task NotifyAsync(Guid recipient, string kind, IReadOnlyDictionary<string, string> data, string? link, CancellationToken ct)
    {
        try
        {
            await notifier.NotifyAsync(new Notification(recipient, kind, data, link), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogNotifyFailed(log, kind, ex);
        }
    }

    /// <summary>The patient's local date (homework days, review dates).</summary>
    public async Task<DateOnly> TodayAsync(Guid patientId, IClock clock, CancellationToken ct)
    {
        var zone = await db.PatientProfiles.AsNoTracking().Where(p => p.PatientId == patientId).Select(p => p.TimeZone).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "Asia/Riyadh";
        return Endpoints.CareEndpoints.Today(zone, clock);
    }

    /// <summary>428 without If-Match, 412 when it names another version ("*" accepts any).</summary>
    public static void RequireVersion(HttpContext http, string current)
    {
        var presented = http.Request.Headers.IfMatch.ToString().Trim();
        if (presented.Length == 0)
        {
            throw new DomainException(ErrorCodes.VersionRequired, "If-Match is required.");
        }

        if (!Matches(presented, current))
        {
            throw new DomainException(ErrorCodes.VersionChanged, "The record changed since it was read.");
        }
    }

    /// <summary>When a client sends If-Match it must match; without one the write is accepted (P37 offline writes).</summary>
    public static void CheckVersionIfPresent(HttpContext http, string current)
    {
        var presented = http.Request.Headers.IfMatch.ToString().Trim();
        if (presented.Length > 0 && !Matches(presented, current))
        {
            throw new DomainException(ErrorCodes.VersionChanged, "The record changed since it was read.");
        }
    }

    public static void SendVersion(HttpContext http, string version) => http.Response.Headers.ETag = $"\"{version}\"";

    public static string Json(object value) => JsonSerializer.Serialize(value, PatientContext.Json);

    private static bool Matches(string presented, string current) =>
        presented == "*" || presented.Split(',').Select(v => v.Trim()).Select(v => v.StartsWith("W/", StringComparison.Ordinal) ? v[2..] : v).Any(v => v.Trim('"') == current);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification {Kind} could not be handed over")]
    private static partial void LogNotifyFailed(ILogger logger, string kind, Exception exception);
}
