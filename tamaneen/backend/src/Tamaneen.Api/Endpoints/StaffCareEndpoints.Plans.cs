using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Tamaneen.Api.Clinical;
using Tamaneen.Api.Patients;
using Tamaneen.Clinical.Care;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Security;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Endpoints;

/// <summary>
/// S15 treatment plan. Every save is a new version (never changed, never deleted), sent with If-Match: the version
/// the therapist started from ("0" before the first plan). A stale version gets 412 and the portal shows both.
/// The first plan needs the approved clinical file (SB-15). Reads and saves are audited.
/// </summary>
public static partial class StaffCareEndpoints
{
    public static readonly IReadOnlySet<string> GoalStatuses = new HashSet<string>(StringComparer.Ordinal) { "not_started", "ongoing", "improving" };

    public sealed record PlanGoalDto(string Text, string Status);

    public sealed record PlanVersionDto(int Version, DateTimeOffset CreatedAt, string AuthorName, IReadOnlyList<PlanGoalDto> Goals, string Interventions, string Frequency, DateOnly ReviewOn, string FollowUpNotes);

    public sealed record PlanVersionSummaryDto(int Version, DateTimeOffset CreatedAt, string AuthorName);

    public sealed record PlanHistoryDto(string PseudonymId, PlanVersionDto? Current, IReadOnlyList<PlanVersionSummaryDto> Versions, bool FileApproved, bool CanEdit);

    public sealed record SavePlanRequest(List<PlanGoalDto>? Goals, string? Interventions, string? Frequency, DateOnly? ReviewOn, string? FollowUpNotes);

    /// <summary>Encrypted content of one plan version.</summary>
    public sealed record StoredPlan(List<PlanGoalDto> Goals, string Interventions, string FollowUpNotes);

    internal static async Task<Ok<PlanHistoryDto>> PlansAsync(string pseudonymId, HttpContext http, ICurrentUser user, CareRecords care, IPatientDataProtector protector, IAuditLog audit, CancellationToken ct)
    {
        var patientId = await StaffEndpoints.PatientIdAsync(care.Db, pseudonymId, ct).ConfigureAwait(false);
        var versions = await care.Db.TreatmentPlanVersions.AsNoTracking().Where(v => v.PatientId == patientId).OrderByDescending(v => v.Version).ToListAsync(ct).ConfigureAwait(false);
        var names = await NamesAsync(care, versions.Select(v => v.TherapistUserId), user.Language, ct).ConfigureAwait(false);
        var current = versions.Count == 0 ? null : await PlanDtoAsync(versions[0], names, protector, ct).ConfigureAwait(false);
        audit.Append("patient.plan_viewed", "UserAccount", patientId.ToString(), StaffEndpoints.Canonical(pseudonymId));
        await care.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        CareRecords.SendVersion(http, (current?.Version ?? 0).ToString(CultureInfo.InvariantCulture));
        return TypedResults.Ok(new PlanHistoryDto(
            StaffEndpoints.Canonical(pseudonymId),
            current,
            versions.Select(v => new PlanVersionSummaryDto(v.Version, v.CreatedAtUtc, names.GetValueOrDefault(v.TherapistUserId, string.Empty))).ToList(),
            await care.FileApprovedAsync(patientId, ct).ConfigureAwait(false),
            user.Role == AppRoles.Therapist));
    }

    /// <summary>SB-20: an older version, read-only.</summary>
    internal static async Task<Ok<PlanVersionDto>> PlanVersionAsync(string pseudonymId, int version, ICurrentUser user, CareRecords care, IPatientDataProtector protector, IAuditLog audit, CancellationToken ct)
    {
        var patientId = await StaffEndpoints.PatientIdAsync(care.Db, pseudonymId, ct).ConfigureAwait(false);
        var row = await care.Db.TreatmentPlanVersions.AsNoTracking().FirstOrDefaultAsync(v => v.PatientId == patientId && v.Version == version, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Plan version {version} not found.");
        audit.Append("patient.plan_viewed", "TreatmentPlanVersion", row.Id.ToString(), StaffEndpoints.Canonical(pseudonymId), $"version={version}");
        await care.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(await PlanDtoAsync(row, await NamesAsync(care, [row.TherapistUserId], user.Language, ct).ConfigureAwait(false), protector, ct).ConfigureAwait(false));
    }

    /// <summary>SB-19: saves a new version. 428 without If-Match, 412 when another version was saved meanwhile (EC-50).</summary>
    internal static async Task<Created<PlanVersionDto>> SavePlanAsync(string pseudonymId, SavePlanRequest request, HttpContext http, ICurrentUser user, CareRecords care, IPatientDataProtector protector, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        CareRecords.RequireTherapist(user);
        var patientId = await StaffEndpoints.PatientIdAsync(care.Db, pseudonymId, ct).ConfigureAwait(false);
        var latest = await care.Db.TreatmentPlanVersions.AsNoTracking().Where(v => v.PatientId == patientId).MaxAsync(v => (int?)v.Version, ct).ConfigureAwait(false) ?? 0;
        CareRecords.RequireVersion(http, latest.ToString(CultureInfo.InvariantCulture));
        if (!await care.FileApprovedAsync(patientId, ct).ConfigureAwait(false))
        {
            throw new DomainException(ErrorCodes.PlanFileNotApproved, "The clinical file must be approved before the first plan.");
        }

        var goals = (request.Goals ?? []).Select(g => new PlanGoalDto(g?.Text?.Trim() ?? string.Empty, g?.Status ?? "not_started")).Where(g => g.Text.Length > 0).ToList();
        if (goals.Count is 0 or > 20 || goals.Sum(g => g.Text.Length) > TreatmentPlanVersion.MaxTextLength || goals.Any(g => !GoalStatuses.Contains(g.Status)))
        {
            throw MeEndpoints.Invalid("goals");
        }

        var interventions = request.Interventions?.Trim() ?? string.Empty;
        if (interventions.Length is 0 or > TreatmentPlanVersion.MaxTextLength)
        {
            throw MeEndpoints.Invalid("interventions");
        }

        var notes = request.FollowUpNotes?.Trim() ?? string.Empty;
        if (notes.Length > TreatmentPlanVersion.MaxTextLength)
        {
            throw MeEndpoints.Invalid("followUpNotes");
        }

        var frequency = request.Frequency switch
        {
            "weekly" => SessionFrequency.Weekly,
            "biweekly" => SessionFrequency.Biweekly,
            "monthly" => SessionFrequency.Monthly,
            _ => throw MeEndpoints.Invalid("frequency"),
        };
        if (request.ReviewOn is not { } reviewOn)
        {
            throw MeEndpoints.Invalid("reviewOn");
        }

        var today = await care.TodayAsync(patientId, clock, ct).ConfigureAwait(false);
        if (reviewOn <= today)
        {
            throw MeEndpoints.Invalid("reviewOn");
        }

        var cipher = await protector.EncryptAsync(patientId, Fields.TreatmentPlan, CareRecords.Json(new StoredPlan(goals, interventions, notes)), ct).ConfigureAwait(false);
        var version = TreatmentPlanVersion.Create(patientId, user.UserId!.Value, latest, cipher, frequency, reviewOn, today, clock);
        care.Db.TreatmentPlanVersions.Add(version);
        audit.Append("patient.plan_saved", "TreatmentPlanVersion", version.Id.ToString(), StaffEndpoints.Canonical(pseudonymId), $"version={version.Version}");
        try
        {
            await care.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_TreatmentPlanVersions_PatientId_Version", StringComparison.Ordinal) == true)
        {
            // Two saves from the same version at once: the first one wins.
            throw new DomainException(ErrorCodes.VersionChanged, "Another plan version was saved concurrently.");
        }

        CareRecords.SendVersion(http, version.Version.ToString(CultureInfo.InvariantCulture));
        var dto = await PlanDtoAsync(version, await NamesAsync(care, [version.TherapistUserId], user.Language, ct).ConfigureAwait(false), protector, ct).ConfigureAwait(false);
        return TypedResults.Created($"/v1/staff/patients/{StaffEndpoints.Canonical(pseudonymId)}/plans/{version.Version}", dto);
    }

    internal static async Task<PlanVersionDto> PlanDtoAsync(TreatmentPlanVersion v, IReadOnlyDictionary<Guid, string> names, IPatientDataProtector protector, CancellationToken ct)
    {
        var stored = JsonSerializer.Deserialize<StoredPlan>(await protector.DecryptAsync(v.PatientId, Fields.TreatmentPlan, v.ContentCipher, ct).ConfigureAwait(false), PatientContext.Json)
            ?? new StoredPlan([], string.Empty, string.Empty);
        return new PlanVersionDto(v.Version, v.CreatedAtUtc, names.GetValueOrDefault(v.TherapistUserId, string.Empty), stored.Goals, stored.Interventions, PlanFrequencyCode(v.Frequency), v.ReviewOn, stored.FollowUpNotes);
    }

    internal static string PlanFrequencyCode(SessionFrequency f) => f switch
    {
        SessionFrequency.Weekly => "weekly",
        SessionFrequency.Biweekly => "biweekly",
        _ => "monthly",
    };

    private static async Task<Dictionary<Guid, string>> NamesAsync(CareRecords care, IEnumerable<Guid> ids, string language, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return list.Count == 0
            ? []
            : await care.Db.StaffProfiles.AsNoTracking().Where(s => list.Contains(s.UserId)).ToDictionaryAsync(s => s.UserId, s => language == "en" ? s.DisplayNameEn : s.DisplayNameAr, ct).ConfigureAwait(false);
    }
}
