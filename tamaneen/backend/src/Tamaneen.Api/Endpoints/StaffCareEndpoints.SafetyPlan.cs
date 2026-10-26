using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamaneen.Api.Clinical;
using Tamaneen.Api.Patients;
using Tamaneen.Clinical.Care;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Security;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Endpoints;

/// <summary>
/// S35 safety-plan builder: the therapist writes the patient's plan with them, in the patient's words. It is the same
/// plan the patient reads and edits in P37 (sections and contacts with phones, encrypted with the patient's key).
/// Concurrency rule: the plan has one version number shared by both sides. Every save, by the patient or the
/// therapist, adds a version; the therapist must send the version they read in If-Match (428 without, 412 when the
/// patient saved meanwhile, and the portal then shows both). Versions are kept and readable, never changed.
/// </summary>
public static partial class StaffCareEndpoints
{
    public sealed record EmergencyLinesDto(string EmergencyNumber, string SupportLine, string TherapistName);

    public sealed record StaffSafetyPlanDto(
        string PseudonymId,
        IReadOnlyDictionary<string, string> Sections,
        IReadOnlyList<CareEndpoints.PlanContactDto> Contacts,
        int Version,
        DateTimeOffset? UpdatedAt,
        string? UpdatedBy,
        string? UpdatedByRole,
        EmergencyLinesDto Emergency,
        bool CanEdit);

    public sealed record SafetyPlanVersionSummaryDto(int Version, DateTimeOffset CreatedAt, string AuthorRole, string AuthorName);

    public sealed record SafetyPlanVersionDto(int Version, DateTimeOffset CreatedAt, string AuthorRole, string AuthorName, IReadOnlyDictionary<string, string> Sections, IReadOnlyList<CareEndpoints.PlanContactDto> Contacts);

    internal static async Task<Ok<StaffSafetyPlanDto>> SafetyPlanAsync(string pseudonymId, HttpContext http, ICurrentUser user, CareRecords care, IPatientDataProtector protector, IOptions<PlatformOptions> platform, IAuditLog audit, CancellationToken ct)
    {
        var patientId = await StaffEndpoints.PatientIdAsync(care.Db, pseudonymId, ct).ConfigureAwait(false);
        audit.Append("patient.safety_plan_viewed", "SafetyPlan", patientId.ToString(), StaffEndpoints.Canonical(pseudonymId));
        await care.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(await StaffPlanDtoAsync(http, pseudonymId, patientId, user, care, protector, platform.Value, ct).ConfigureAwait(false));
    }

    internal static async Task<Ok<List<SafetyPlanVersionSummaryDto>>> SafetyPlanVersionsAsync(string pseudonymId, ICurrentUser user, CareRecords care, CancellationToken ct)
    {
        var patientId = await StaffEndpoints.PatientIdAsync(care.Db, pseudonymId, ct).ConfigureAwait(false);
        var rows = await care.Db.SafetyPlanVersions.AsNoTracking().Where(v => v.PatientId == patientId).OrderByDescending(v => v.Version).ToListAsync(ct).ConfigureAwait(false);
        var names = await NamesAsync(care, rows.Select(r => r.AuthorUserId), user.Language, ct).ConfigureAwait(false);
        return TypedResults.Ok(rows.Select(r => new SafetyPlanVersionSummaryDto(r.Version, r.CreatedAtUtc, RoleOf(r.AuthorRole), names.GetValueOrDefault(r.AuthorUserId, string.Empty))).ToList());
    }

    internal static async Task<Ok<SafetyPlanVersionDto>> SafetyPlanVersionAsync(string pseudonymId, int version, ICurrentUser user, CareRecords care, IPatientDataProtector protector, IAuditLog audit, CancellationToken ct)
    {
        var patientId = await StaffEndpoints.PatientIdAsync(care.Db, pseudonymId, ct).ConfigureAwait(false);
        var row = await care.Db.SafetyPlanVersions.AsNoTracking().FirstOrDefaultAsync(v => v.PatientId == patientId && v.Version == version, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Safety plan version {version} not found.");
        var stored = CareEndpoints.ReadStoredPlan(await protector.DecryptAsync(patientId, Fields.SafetyPlan, row.SectionsCipher, ct).ConfigureAwait(false));
        audit.Append("patient.safety_plan_viewed", "SafetyPlanVersion", row.Id.ToString(), StaffEndpoints.Canonical(pseudonymId), $"version={version}");
        await care.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        var names = await NamesAsync(care, [row.AuthorUserId], user.Language, ct).ConfigureAwait(false);
        return TypedResults.Ok(new SafetyPlanVersionDto(row.Version, row.CreatedAtUtc, RoleOf(row.AuthorRole), names.GetValueOrDefault(row.AuthorUserId, string.Empty), Complete(stored.Sections), stored.Contacts ?? []));
    }

    /// <summary>"Save and send to the patient": a new version the patient's app stores on the device. The patient is notified.</summary>
    internal static async Task<Created<StaffSafetyPlanDto>> SaveSafetyPlanAsync(string pseudonymId, CareEndpoints.UpdateSafetyPlanRequest request, HttpContext http, ICurrentUser user, CareRecords care, IPatientDataProtector protector, IOptions<PlatformOptions> platform, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        CareRecords.RequireTherapist(user);
        var patientId = await StaffEndpoints.PatientIdAsync(care.Db, pseudonymId, ct).ConfigureAwait(false);
        var (sections, contacts) = CareEndpoints.CleanPlan(request);
        var plan = await care.Db.SafetyPlans.FirstOrDefaultAsync(s => s.PatientId == patientId, ct).ConfigureAwait(false);
        CareRecords.RequireVersion(http, (plan?.Version ?? 0).ToString(CultureInfo.InvariantCulture));
        if (plan is null)
        {
            plan = SafetyPlan.Create(patientId);
            care.Db.SafetyPlans.Add(plan);
        }

        // Written by the clinician with the patient: not a patient disclosure, so it does not pass the crisis gate.
        plan.Update(await protector.EncryptAsync(patientId, Fields.SafetyPlan, CareRecords.Json(new CareEndpoints.StoredSafetyPlan(sections, contacts)), ct).ConfigureAwait(false), user.UserId!.Value, user.Role!, clock);
        care.Db.SafetyPlanVersions.Add(SafetyPlanVersion.Of(plan));
        audit.Append("safety_plan.updated", "SafetyPlan", patientId.ToString(), StaffEndpoints.Canonical(pseudonymId), $"version={plan.Version}");
        await CareEndpoints.SavePlanOrConflictAsync(care.Db, ct).ConfigureAwait(false);
        await care.NotifyAsync(patientId, "safety_plan.updated", new Dictionary<string, string> { ["version"] = plan.Version.ToString(CultureInfo.InvariantCulture), ["by"] = "therapist" }, "/safety-plan", ct).ConfigureAwait(false);
        var dto = await StaffPlanDtoAsync(http, pseudonymId, patientId, user, care, protector, platform.Value, ct).ConfigureAwait(false);
        return TypedResults.Created($"/v1/staff/patients/{StaffEndpoints.Canonical(pseudonymId)}/safety-plan/versions/{dto.Version}", dto);
    }

    private static async Task<StaffSafetyPlanDto> StaffPlanDtoAsync(HttpContext http, string pseudonymId, Guid patientId, ICurrentUser user, CareRecords care, IPatientDataProtector protector, PlatformOptions platform, CancellationToken ct)
    {
        var plan = await care.Db.SafetyPlans.AsNoTracking().FirstOrDefaultAsync(s => s.PatientId == patientId, ct).ConfigureAwait(false);
        var therapists = await care.ActiveTherapistsAsync(patientId, ct).ConfigureAwait(false);
        var treating = user.Role == AppRoles.Therapist ? user.UserId : therapists.FirstOrDefault();
        var emergency = new EmergencyLinesDto(platform.EmergencyNumber, platform.SupportLine, await care.StaffNameAsync(treating, user.Language, ct).ConfigureAwait(false));
        CareRecords.SendVersion(http, (plan?.Version ?? 0).ToString(CultureInfo.InvariantCulture));
        if (plan is null)
        {
            return new StaffSafetyPlanDto(StaffEndpoints.Canonical(pseudonymId), Complete([]), [], 0, null, null, null, emergency, user.Role == AppRoles.Therapist);
        }

        var stored = CareEndpoints.ReadStoredPlan(await protector.DecryptAsync(patientId, Fields.SafetyPlan, plan.SectionsCipher, ct).ConfigureAwait(false));
        return new StaffSafetyPlanDto(
            StaffEndpoints.Canonical(pseudonymId),
            Complete(stored.Sections),
            stored.Contacts ?? [],
            plan.Version,
            plan.UpdatedAtUtc,
            plan.UpdatedByRole == AppRoles.Patient ? null : await care.StaffNameAsync(plan.UpdatedByUserId, user.Language, ct).ConfigureAwait(false),
            RoleOf(plan.UpdatedByRole),
            emergency,
            user.Role == AppRoles.Therapist);
    }

    private static string RoleOf(string role) => role == AppRoles.Patient ? "patient" : StaffEndpoints.RoleCode(role);

    private static Dictionary<string, string> Complete(Dictionary<string, string> sections) =>
        SafetyPlan.Sections.ToDictionary(s => s, s => sections.GetValueOrDefault(s) ?? string.Empty);
}
