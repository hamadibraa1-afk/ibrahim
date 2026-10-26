using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Tamaneen.Api.Clinical;
using Tamaneen.Api.Patients;
using Tamaneen.Clinical.Care;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Security;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Endpoints;

/// <summary>
/// S57 end of care. The therapist drafts the closing summary (If-Match with its revision) and sends it to a
/// supervisor; the API refuses to send or sign off while the risk was 3–4 in the last 14 days (§10.4). The sign-off
/// ends the care assignment (CareAssignment.EndedAtUtc), so row-level security stops the therapist reading new
/// data, cancels the remaining sessions with that therapist and tells the patient (P53).
/// </summary>
public static partial class StaffCareEndpoints
{
    public sealed record MeasureDto(string Instrument, int? Start, int? End);

    public sealed record ClosureDto(
        Guid Id,
        string Status,
        string ReasonCode,
        int SessionCount,
        int DurationWeeks,
        string PatientSummary,
        string WarningSigns,
        string WhatToDo,
        string FollowUp,
        bool KeepLibrary,
        DateTimeOffset? SubmittedAt,
        string? RejectionReason,
        string? DecidedBy,
        DateTimeOffset? DecidedAt,
        int Revision);

    public sealed record ClosurePrefillDto(int SessionCount, int DurationWeeks);

    public sealed record ClosureViewDto(string PseudonymId, ClosureDto? Closure, ClosurePrefillDto Prefill, IReadOnlyList<MeasureDto> Measures, bool Blocked, bool CanEdit, bool CanSignOff, string TherapistName);

    public sealed record SaveClosureRequest(string? ReasonCode, int? SessionCount, int? DurationWeeks, string? PatientSummary, string? WarningSigns, string? WhatToDo, string? FollowUp, bool? KeepLibrary, bool Submit);

    public sealed record ClosureQueueItemDto(Guid Id, string PseudonymId, string TherapistName, string ReasonCode, int SessionCount, DateTimeOffset SubmittedAt, bool Blocked);

    public sealed record SignOffRequest(bool Approve, string? Reason);

    /// <summary>Relapse-prevention plan, encrypted.</summary>
    public sealed record StoredRelapsePlan(string WarningSigns, string WhatToDo);

    internal static async Task<Ok<ClosureViewDto>> ClosureAsync(string pseudonymId, HttpContext http, ICurrentUser user, CareRecords care, IPatientDataProtector protector, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var patientId = await StaffEndpoints.PatientIdAsync(care.Db, pseudonymId, ct).ConfigureAwait(false);
        var closure = await CurrentClosureAsync(care, patientId, user, tracked: false, ct).ConfigureAwait(false);
        audit.Append("patient.closure_viewed", "UserAccount", patientId.ToString(), StaffEndpoints.Canonical(pseudonymId));
        await care.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(await ClosureViewAsync(http, pseudonymId, patientId, closure, user, care, protector, clock, ct).ConfigureAwait(false));
    }

    /// <summary>Saves the draft; with submit = true it also sends it for sign-off (required fields and the risk rule are checked).</summary>
    internal static async Task<Ok<ClosureViewDto>> SaveClosureAsync(string pseudonymId, SaveClosureRequest request, HttpContext http, ICurrentUser user, CareRecords care, IPatientDataProtector protector, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        CareRecords.RequireTherapist(user);
        var patientId = await StaffEndpoints.PatientIdAsync(care.Db, pseudonymId, ct).ConfigureAwait(false);
        var closure = await CurrentClosureAsync(care, patientId, user, tracked: true, ct).ConfigureAwait(false);
        if (closure is { Status: CareClosureStatus.SignedOff })
        {
            closure = null;
        }

        CareRecords.RequireVersion(http, (closure?.Revision ?? 0).ToString(CultureInfo.InvariantCulture));
        var summary = request.PatientSummary?.Trim() ?? string.Empty;
        var signs = request.WarningSigns?.Trim() ?? string.Empty;
        var todo = request.WhatToDo?.Trim() ?? string.Empty;
        var reason = request.ReasonCode?.Trim() ?? string.Empty;
        var followUp = string.IsNullOrWhiteSpace(request.FollowUp) ? "none" : request.FollowUp.Trim();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (summary.Length > CareClosure.MaxPatientSummary)
        {
            errors["patientSummary"] = ["invalid"];
        }

        if (signs.Length > CareClosure.MaxPlanField)
        {
            errors["warningSigns"] = ["invalid"];
        }

        if (todo.Length > CareClosure.MaxPlanField)
        {
            errors["whatToDo"] = ["invalid"];
        }

        if (reason.Length > 0 && !CareClosure.Reasons.Contains(reason))
        {
            errors["reasonCode"] = ["invalid"];
        }

        if (!CareClosure.FollowUps.Contains(followUp))
        {
            errors["followUp"] = ["invalid"];
        }

        if (request.Submit)
        {
            foreach (var (key, value) in new[] { ("reasonCode", reason), ("patientSummary", summary), ("warningSigns", signs), ("whatToDo", todo) })
            {
                if (value.Length == 0)
                {
                    errors.TryAdd(key, ["required"]);
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new DomainException(ErrorCodes.Validation, "Invalid closure.").With("errors", errors);
        }

        if (closure is null)
        {
            closure = CareClosure.Start(patientId, user.UserId!.Value, clock);
            care.Db.CareClosures.Add(closure);
        }

        var prefill = await PrefillAsync(care, patientId, clock, ct).ConfigureAwait(false);
        closure.Save(
            reason,
            request.SessionCount ?? prefill.SessionCount,
            request.DurationWeeks ?? prefill.DurationWeeks,
            summary.Length == 0 ? null : await protector.EncryptAsync(patientId, Fields.ClosurePatientSummary, summary, ct).ConfigureAwait(false),
            signs.Length == 0 && todo.Length == 0 ? null : await protector.EncryptAsync(patientId, Fields.ClosureRelapsePlan, CareRecords.Json(new StoredRelapsePlan(signs, todo)), ct).ConfigureAwait(false),
            followUp,
            request.KeepLibrary ?? true,
            clock);
        if (request.Submit)
        {
            if (await care.ClosureBlockedAsync(patientId, clock, ct).ConfigureAwait(false))
            {
                throw new DomainException(ErrorCodes.ClosureBlocked, "Risk 3–4 in the last 14 days.");
            }

            closure.Submit(clock);
        }

        audit.Append(request.Submit ? "care.closure_submitted" : "care.closure_saved", "CareClosure", closure.Id.ToString(), StaffEndpoints.Canonical(pseudonymId), reason.Length == 0 ? null : $"reason={reason}");
        await SaveClosureChangesAsync(care, ct).ConfigureAwait(false);
        if (request.Submit)
        {
            foreach (var supervisor in await care.SupervisorIdsAsync(ct).ConfigureAwait(false))
            {
                await care.NotifyAsync(supervisor, "care.closure_submitted", new Dictionary<string, string> { ["closureId"] = closure.Id.ToString(), ["pseudonymId"] = StaffEndpoints.Canonical(pseudonymId) },
                    $"/patients/{StaffEndpoints.Canonical(pseudonymId)}/close", ct).ConfigureAwait(false);
            }
        }

        return TypedResults.Ok(await ClosureViewAsync(http, pseudonymId, patientId, closure, user, care, protector, clock, ct).ConfigureAwait(false));
    }

    /// <summary>"Withdraw": a submitted summary goes back to draft.</summary>
    internal static async Task<Ok<ClosureViewDto>> WithdrawClosureAsync(string pseudonymId, HttpContext http, ICurrentUser user, CareRecords care, IPatientDataProtector protector, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        CareRecords.RequireTherapist(user);
        var patientId = await StaffEndpoints.PatientIdAsync(care.Db, pseudonymId, ct).ConfigureAwait(false);
        var closure = await CurrentClosureAsync(care, patientId, user, tracked: true, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, "No end-of-care summary.");
        closure.Withdraw(clock);
        audit.Append("care.closure_withdrawn", "CareClosure", closure.Id.ToString(), StaffEndpoints.Canonical(pseudonymId));
        await SaveClosureChangesAsync(care, ct).ConfigureAwait(false);
        return TypedResults.Ok(await ClosureViewAsync(http, pseudonymId, patientId, closure, user, care, protector, clock, ct).ConfigureAwait(false));
    }

    /// <summary>Summaries waiting for a supervisor's sign-off, oldest first.</summary>
    internal static async Task<Ok<List<ClosureQueueItemDto>>> ClosureQueueAsync(ICurrentUser user, CareRecords care, IClock clock, CancellationToken ct)
    {
        var rows = await care.Db.CareClosures.AsNoTracking().Where(c => c.Status == CareClosureStatus.Submitted).OrderBy(c => c.SubmittedAtUtc).Take(200).ToListAsync(ct).ConfigureAwait(false);
        var ids = rows.Select(r => r.PatientId).ToList();
        var pseudonyms = await care.Db.UserAccounts.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.PseudonymId, ct).ConfigureAwait(false);
        var names = await NamesAsync(care, rows.Select(r => r.TherapistUserId), user.Language, ct).ConfigureAwait(false);
        var items = new List<ClosureQueueItemDto>();
        foreach (var r in rows)
        {
            items.Add(new ClosureQueueItemDto(r.Id, pseudonyms.GetValueOrDefault(r.PatientId, string.Empty), names.GetValueOrDefault(r.TherapistUserId, string.Empty), r.ReasonCode, r.SessionCount, r.SubmittedAtUtc!.Value,
                await care.ClosureBlockedAsync(r.PatientId, clock, ct).ConfigureAwait(false)));
        }

        return TypedResults.Ok(items);
    }

    /// <summary>
    /// The supervisor signs off (SignedOffBy) or sends it back with a reason. Signing off ends the care assignment,
    /// cancels the patient's remaining sessions with the therapist and tells the patient how to come back.
    /// </summary>
    internal static async Task<Ok<ClosureDto>> SignOffAsync(Guid id, SignOffRequest request, ICurrentUser user, CareRecords care, IPatientDataProtector protector, WaitlistNotifier waitlist, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var closure = await care.Db.CareClosures.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Closure {id} not found.");
        var pseudonym = await PseudonymAsync(care, closure.PatientId, ct).ConfigureAwait(false);
        var me = user.UserId!.Value;
        var freedSlots = false;
        if (request.Approve)
        {
            if (await care.ClosureBlockedAsync(closure.PatientId, clock, ct).ConfigureAwait(false))
            {
                throw new DomainException(ErrorCodes.ClosureBlocked, "Risk 3–4 in the last 14 days.");
            }

            closure.SignOff(me, clock);
            foreach (var assignment in await care.Db.CareAssignments.Where(a => a.PatientId == closure.PatientId && a.TherapistUserId == closure.TherapistUserId && a.EndedAtUtc == null).ToListAsync(ct).ConfigureAwait(false))
            {
                assignment.End("care_closed", clock);
            }

            var now = clock.UtcNow;
            var upcoming = await care.Db.Appointments.Where(a => a.PatientId == closure.PatientId && a.TherapistUserId == closure.TherapistUserId && a.Status == AppointmentStatus.Confirmed && a.StartsAtUtc > now).ToListAsync(ct).ConfigureAwait(false);
            foreach (var appointment in upcoming)
            {
                appointment.CancelByTherapist(clock);
                (await care.Db.AvailabilitySlots.FirstAsync(s => s.Id == appointment.SlotId, ct).ConfigureAwait(false)).Free();
                freedSlots = true;
            }

            audit.Append("care.closure_signed_off", "CareClosure", closure.Id.ToString(), pseudonym, $"cancelled={upcoming.Count}");
            audit.Append("assignment.ended", "CareAssignment", null, pseudonym, "care_closed");
        }
        else
        {
            var reason = request.Reason?.Trim();
            if (string.IsNullOrEmpty(reason))
            {
                throw new DomainException(ErrorCodes.ReasonRequired, "A reason is required to send it back.");
            }

            if (reason.Length > 1000)
            {
                throw MeEndpoints.Invalid("reason");
            }

            closure.Reject(me, await protector.EncryptAsync(closure.PatientId, Fields.ClosureRejection, reason, ct).ConfigureAwait(false), clock);
            audit.Append("care.closure_rejected", "CareClosure", closure.Id.ToString(), pseudonym);
        }

        await SaveClosureChangesAsync(care, ct).ConfigureAwait(false);
        if (request.Approve)
        {
            await care.NotifyAsync(closure.PatientId, "care.ended", new Dictionary<string, string> { ["closureId"] = closure.Id.ToString() }, "/care/complete", ct).ConfigureAwait(false);
            if (freedSlots)
            {
                await waitlist.CheckAsync(ct).ConfigureAwait(false);
            }
        }
        else
        {
            await care.NotifyAsync(closure.TherapistUserId, "care.closure_rejected", new Dictionary<string, string> { ["closureId"] = closure.Id.ToString(), ["pseudonymId"] = pseudonym }, $"/patients/{pseudonym}/close", ct).ConfigureAwait(false);
        }

        return TypedResults.Ok(await ClosureDtoAsync(closure, user, care, protector, ct).ConfigureAwait(false));
    }

    /// <summary>A therapist's own latest summary for the patient; for a supervisor, the latest one.</summary>
    private static async Task<CareClosure?> CurrentClosureAsync(CareRecords care, Guid patientId, ICurrentUser user, bool tracked, CancellationToken ct)
    {
        var query = tracked ? care.Db.CareClosures : care.Db.CareClosures.AsNoTracking();
        query = query.Where(c => c.PatientId == patientId);
        if (user.Role == AppRoles.Therapist)
        {
            query = query.Where(c => c.TherapistUserId == user.UserId);
        }

        return await query.OrderByDescending(c => c.CreatedAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Sessions attended and weeks since the first one (editable in the form).</summary>
    private static async Task<ClosurePrefillDto> PrefillAsync(CareRecords care, Guid patientId, IClock clock, CancellationToken ct)
    {
        var attended = await care.Db.Appointments.AsNoTracking().Where(a => a.PatientId == patientId && a.Status == AppointmentStatus.Completed).Select(a => a.StartsAtUtc).ToListAsync(ct).ConfigureAwait(false);
        var started = await care.Db.CareAssignments.AsNoTracking().Where(a => a.PatientId == patientId).OrderBy(a => a.StartedAtUtc).Select(a => (DateTimeOffset?)a.StartedAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var from = attended.Count > 0 ? attended.Min() : started ?? clock.UtcNow;
        return new ClosurePrefillDto(attended.Count, Math.Max(0, (int)((clock.UtcNow - from).TotalDays / 7)));
    }

    private static async Task<ClosureViewDto> ClosureViewAsync(HttpContext http, string pseudonymId, Guid patientId, CareClosure? closure, ICurrentUser user, CareRecords care, IPatientDataProtector protector, IClock clock, CancellationToken ct)
    {
        var triage = await care.ClinicalTriageAsync(patientId, ct).ConfigureAwait(false);
        var measures = new List<MeasureDto> { new("PHQ-9", triage?.Phq9, null), new("GAD-7", triage?.Gad7, null) };
        var dto = closure is null ? null : await ClosureDtoAsync(closure, user, care, protector, ct).ConfigureAwait(false);
        CareRecords.SendVersion(http, (closure is { Status: not CareClosureStatus.SignedOff } ? closure.Revision : 0).ToString(CultureInfo.InvariantCulture));
        var therapist = closure?.TherapistUserId ?? (user.Role == AppRoles.Therapist ? user.UserId : (await care.ActiveTherapistsAsync(patientId, ct).ConfigureAwait(false)).FirstOrDefault());
        return new ClosureViewDto(
            StaffEndpoints.Canonical(pseudonymId),
            dto,
            await PrefillAsync(care, patientId, clock, ct).ConfigureAwait(false),
            measures,
            await care.ClosureBlockedAsync(patientId, clock, ct).ConfigureAwait(false),
            user.Role == AppRoles.Therapist,
            user.Role is AppRoles.Supervisor or AppRoles.ClinicalLead && closure is { Status: CareClosureStatus.Submitted } && closure.TherapistUserId != user.UserId,
            await care.StaffNameAsync(therapist, user.Language, ct).ConfigureAwait(false));
    }

    private static async Task<ClosureDto> ClosureDtoAsync(CareClosure c, ICurrentUser user, CareRecords care, IPatientDataProtector protector, CancellationToken ct)
    {
        var plan = c.RelapsePlanCipher is null ? null : JsonSerializer.Deserialize<StoredRelapsePlan>(await protector.DecryptAsync(c.PatientId, Fields.ClosureRelapsePlan, c.RelapsePlanCipher, ct).ConfigureAwait(false), PatientContext.Json);
        return new ClosureDto(
            c.Id,
            ClosureStatusCode(c.Status),
            c.ReasonCode,
            c.SessionCount,
            c.DurationWeeks,
            c.PatientSummaryCipher is null ? string.Empty : await protector.DecryptAsync(c.PatientId, Fields.ClosurePatientSummary, c.PatientSummaryCipher, ct).ConfigureAwait(false),
            plan?.WarningSigns ?? string.Empty,
            plan?.WhatToDo ?? string.Empty,
            c.FollowUp,
            c.KeepLibrary,
            c.SubmittedAtUtc,
            c.RejectionCipher is null ? null : await protector.DecryptAsync(c.PatientId, Fields.ClosureRejection, c.RejectionCipher, ct).ConfigureAwait(false),
            c.DecidedByUserId is { } by ? await care.StaffNameAsync(by, user.Language, ct).ConfigureAwait(false) : null,
            c.DecidedAtUtc,
            c.Revision);
    }

    internal static string ClosureStatusCode(CareClosureStatus s) => s switch
    {
        CareClosureStatus.Draft => "draft",
        CareClosureStatus.Submitted => "submitted",
        CareClosureStatus.SignedOff => "signed_off",
        _ => "rejected",
    };

    private static async Task SaveClosureChangesAsync(CareRecords care, CancellationToken ct)
    {
        try
        {
            await care.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException(ErrorCodes.VersionChanged, "The summary changed concurrently.");
        }
    }
}
