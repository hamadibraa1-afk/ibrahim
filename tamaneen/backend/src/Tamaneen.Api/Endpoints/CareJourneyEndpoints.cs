using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Tamaneen.Api.Clinical;
using Tamaneen.Api.Patients;
using Tamaneen.Clinical.Care;
using Tamaneen.Clinical.Intake;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Hosting;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Endpoints;

/// <summary>
/// The patient's side of changing therapist (P44) and of the end of care (P53).
/// P44: a request opens a triage case with path "change" in the supervisors' S03 queue, due within
/// Platform:ChangeTherapistWithinHours. The supervisor approves new therapists there (never the current one); the
/// patient books one from P07, and the booking ends the old assignment. The current therapist is never told the reason.
/// P53: after a supervisor signs off the closing summary the patient reads it, downloads it and can come back.
/// </summary>
public static class CareJourneyEndpoints
{
    public sealed record ReasonDto(string Code, string Label);

    public sealed record ChangeRequestBody(string? ReasonCode);

    public sealed record ChangeRequestDto(Guid Id, string Status, string? ReasonCode, DateTimeOffset CreatedAt, DateTimeOffset ExpectedBy, int WithinHours);

    public sealed record PatientClosureDto(string TherapistName, int SessionCount, string Summary, string? WarningSigns, string? WhatToDo, string FollowUp, bool KeepLibrary, DateTimeOffset ClosedAt, bool ReturnRequested);

    public sealed record ReturnResponse(string Next);

    public static IEndpointRouteBuilder MapCareJourneyEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1").RequireAuthorization(AuthSetup.PatientPolicy);
        g.MapGet("/catalogs/change-reasons", ReasonsAsync);
        g.MapGet("/me/change-therapist-requests/current", CurrentRequestAsync);
        g.MapPost("/me/change-therapist-requests", CreateRequestAsync).WithIdempotency();
        g.MapGet("/me/care-closure", ClosureAsync);
        g.MapGet("/me/care-closure/summary", SummaryFileAsync);
        g.MapPost("/me/care/return", ReturnAsync);
        return app;
    }

    // ---------- P44 ----------

    /// <summary>The five reasons of the change-reason catalog, in the caller's language.</summary>
    internal static Ok<List<ReasonDto>> ReasonsAsync(PatientContext p, IStringLocalizer<SharedResource> text) =>
        TypedResults.Ok(LocalizationSetup.WithLanguage(p.Language, () => ChangeTherapistRequest.ReasonCodes.Select(c => new ReasonDto(c, text[$"catalog.change_reason.{c}"].Value)).ToList()));

    internal static async Task<Ok<ChangeRequestDto>> CurrentRequestAsync(PatientContext p, IOptions<PlatformOptions> platform, IClock clock, CancellationToken ct)
    {
        var request = await p.Db.ChangeTherapistRequests.Where(r => r.PatientId == p.PatientId && r.Status == ChangeTherapistStatus.Requested).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, "No open change request.");
        return TypedResults.Ok(await DtoAsync(p, request, platform.Value, clock, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// Opens the request: no reason needed and never free text. A second open request is refused with the current one
    /// (409 error.change_request.open + current). The current appointment stays booked until the patient chooses.
    /// </summary>
    internal static async Task<Created<ChangeRequestDto>> CreateRequestAsync(ChangeRequestBody? body, PatientContext p, CareRecords care, IOptions<PlatformOptions> platform, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var reason = string.IsNullOrWhiteSpace(body?.ReasonCode) ? null : body.ReasonCode.Trim();
        if (reason is not null && !ChangeTherapistRequest.ReasonCodes.Contains(reason))
        {
            throw MeEndpoints.Invalid("reasonCode");
        }

        var open = await p.Db.ChangeTherapistRequests.FirstOrDefaultAsync(r => r.PatientId == p.PatientId && r.Status == ChangeTherapistStatus.Requested, ct).ConfigureAwait(false);
        if (open is not null)
        {
            throw new DomainException(ErrorCodes.ChangeRequestOpen, "A change request is already open.").With("current", await DtoAsync(p, open, platform.Value, clock, ct).ConfigureAwait(false));
        }

        var current = await p.Db.CareAssignments.AsNoTracking().Where(a => a.PatientId == p.PatientId && a.EndedAtUtc == null).OrderByDescending(a => a.StartedAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotInCare, "No current therapist.");
        var clinical = await care.ClinicalTriageAsync(p.PatientId, ct).ConfigureAwait(false);
        var triage = TriageCase.ForChange(clinical, p.PatientId, TimeSpan.FromHours(platform.Value.ChangeTherapistWithinHours), clock);
        p.Db.TriageCases.Add(triage);

        // The supervisor proposes fresh suggestions; the old ones (which may include the current therapist) go.
        p.Db.MatchSuggestions.RemoveRange(await p.Db.MatchSuggestions.Where(m => m.PatientId == p.PatientId).ToListAsync(ct).ConfigureAwait(false));
        var request = ChangeTherapistRequest.Create(p.PatientId, current.TherapistUserId, reason, triage.Id, clock);
        p.Db.ChangeTherapistRequests.Add(request);
        var pseudonym = (await p.AccountAsync(ct).ConfigureAwait(false)).PseudonymId;
        audit.Append("care.change_requested", "ChangeTherapistRequest", request.Id.ToString(), pseudonym, reason is null ? null : $"reason={reason}");
        try
        {
            await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UX_ChangeTherapistRequests_OneOpen", StringComparison.Ordinal) == true)
        {
            throw new DomainException(ErrorCodes.ChangeRequestOpen, "A change request was opened concurrently.");
        }

        foreach (var supervisor in await care.SupervisorIdsAsync(ct).ConfigureAwait(false))
        {
            await care.NotifyAsync(supervisor, "care.change_requested", new Dictionary<string, string>
            {
                ["triageCaseId"] = triage.Id.ToString(),
                ["pseudonymId"] = pseudonym,
                ["dueAt"] = triage.DueAtUtc.ToString("O", CultureInfo.InvariantCulture),
            }, $"/triage/{triage.Id}", ct).ConfigureAwait(false);
        }

        return TypedResults.Created("/v1/me/change-therapist-requests/current", await DtoAsync(p, request, platform.Value, clock, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// Called after a confirmed booking (P08). With an open change request and a new therapist, the old assignment
    /// ends (scheduling service), the request is accepted, the old therapist's sessions that can still be changed are
    /// cancelled, and the old therapist is told, without the reason.
    /// </summary>
    internal static async Task CompleteTransferAsync(PatientContext p, ScopeRunner scopes, CareRecords care, Guid newTherapistId, string pseudonym, IClock clock, CancellationToken ct)
    {
        var request = await p.Db.ChangeTherapistRequests.AsNoTracking().FirstOrDefaultAsync(r => r.PatientId == p.PatientId && r.Status == ChangeTherapistStatus.Requested, ct).ConfigureAwait(false);
        if (request is null || request.FromTherapistUserId == newTherapistId)
        {
            return;
        }

        var patientId = p.PatientId;
        var old = request.FromTherapistUserId;
        await scopes.AsServiceAsync(SchedulingEndpoints.SchedulingServiceId, AppRoles.SchedulingService, async sp =>
        {
            var sdb = sp.GetRequiredService<TamaneenDbContext>();
            foreach (var assignment in await sdb.CareAssignments.Where(a => a.PatientId == patientId && a.TherapistUserId == old && a.EndedAtUtc == null).ToListAsync(ct).ConfigureAwait(false))
            {
                assignment.End("changed_therapist", clock);
            }

            var tracked = await sdb.ChangeTherapistRequests.FirstAsync(r => r.Id == request.Id, ct).ConfigureAwait(false);
            tracked.Accept(newTherapistId, clock);
            sp.GetRequiredService<IAuditLog>().Append("assignment.ended", "CareAssignment", null, pseudonym, "changed_therapist");
            await sdb.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);

        var now = clock.UtcNow;
        var remaining = await p.Db.Appointments.Where(a => a.PatientId == patientId && a.TherapistUserId == old && a.Status == AppointmentStatus.Confirmed && a.StartsAtUtc > now).ToListAsync(ct).ConfigureAwait(false);
        foreach (var appointment in remaining.Where(a => a.CanChange(clock)))
        {
            appointment.CancelByPatient("therapist_fit", clock);
            (await p.Db.AvailabilitySlots.FirstAsync(s => s.Id == appointment.SlotId, ct).ConfigureAwait(false)).Free();
        }

        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        await care.NotifyAsync(old, "care.transferred", new Dictionary<string, string> { ["pseudonymId"] = pseudonym }, null, ct).ConfigureAwait(false);
    }

    /// <summary>Requested until a supervisor holds the case (matching), offered once therapists were approved.</summary>
    private static async Task<ChangeRequestDto> DtoAsync(PatientContext p, ChangeTherapistRequest r, PlatformOptions platform, IClock clock, CancellationToken ct)
    {
        var triage = await p.Db.TriageCases.AsNoTracking().FirstOrDefaultAsync(t => t.Id == r.TriageCaseId, ct).ConfigureAwait(false);
        var status = r.Status switch
        {
            ChangeTherapistStatus.Accepted => "accepted",
            ChangeTherapistStatus.Closed => "closed",
            _ => triage?.Status switch
            {
                TriageStatus.Reviewed or TriageStatus.Matched => "offered",
                TriageStatus.Closed or TriageStatus.Escalated => "closed",
                TriageStatus.Pending when triage.ClaimedUntilUtc > clock.UtcNow => "matching",
                _ => "requested",
            },
        };
        return new ChangeRequestDto(r.Id, status, r.ReasonCode, r.CreatedAtUtc, triage?.DueAtUtc ?? r.CreatedAtUtc.AddHours(platform.ChangeTherapistWithinHours), platform.ChangeTherapistWithinHours);
    }

    // ---------- P53 ----------

    internal static async Task<Ok<PatientClosureDto>> ClosureAsync(PatientContext p, CancellationToken ct)
    {
        var (closure, dto) = await SignedOffAsync(p, ct).ConfigureAwait(false);
        _ = closure;
        return TypedResults.Ok(dto);
    }

    /// <summary>"Download my summary": the plain-language summary and relapse plan, no diagnosis or scores. Audited.</summary>
    internal static async Task<IResult> SummaryFileAsync(PatientContext p, IStringLocalizer<SharedResource> text, IAuditLog audit, CancellationToken ct)
    {
        var (closure, dto) = await SignedOffAsync(p, ct).ConfigureAwait(false);
        string T(string key, params object[] args) => LocalizationSetup.WithLanguage(p.Language, () => text[key, args].Value);
        var body = new StringBuilder()
            .AppendLine(T("closure.summary.title"))
            .AppendLine()
            .AppendLine(T("closure.summary.sessions", dto.SessionCount))
            .AppendLine()
            .AppendLine(dto.Summary);
        if (dto.WarningSigns is not null || dto.WhatToDo is not null)
        {
            body.AppendLine().AppendLine(T("closure.summary.plan"));
            if (!string.IsNullOrEmpty(dto.WarningSigns))
            {
                body.AppendLine(T("closure.summary.signs", dto.WarningSigns));
            }

            if (!string.IsNullOrEmpty(dto.WhatToDo))
            {
                body.AppendLine(T("closure.summary.do", dto.WhatToDo));
            }
        }

        body.AppendLine().AppendLine(T("closure.summary.return"));
        audit.Append("care.summary_downloaded", "CareClosure", closure.Id.ToString(), (await p.AccountAsync(ct).ConfigureAwait(false)).PseudonymId);
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(body.ToString())).ToArray(), "text/plain; charset=utf-8", "summary.txt");
    }

    /// <summary>How recent the P23 safety check before a return (P53) must be.</summary>
    public static readonly TimeSpan ReturnSafetyCheckWindow = TimeSpan.FromHours(1);

    /// <summary>
    /// "I need to come back": matching reopens with the previous therapist first, no new sign-up. The design asks for a
    /// short safety check first, so the patient answers P23 again (same C-SSRS screener, same crisis gate): the return
    /// needs a screening completed after the sign-off and within <see cref="ReturnSafetyCheckWindow"/>, below High,
    /// and no crisis lock (409 error.care.safety_check_required otherwise). The screening is named in the audit row.
    /// </summary>
    internal static async Task<Ok<ReturnResponse>> ReturnAsync(PatientContext p, TherapistDirectory directory, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var (closure, _) = await SignedOffAsync(p, ct).ConfigureAwait(false);
        await p.EnsureNotLockedAsync(ct).ConfigureAwait(false);
        var since = clock.UtcNow - ReturnSafetyCheckWindow;
        var check = await p.Db.Screenings.AsNoTracking()
            .Where(s => s.PatientId == p.PatientId && s.CompletedAtUtc >= since && (closure.DecidedAtUtc == null || s.CompletedAtUtc >= closure.DecidedAtUtc))
            .OrderByDescending(s => s.CompletedAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (check is null || check.Level >= RiskLevel.High)
        {
            throw new DomainException(ErrorCodes.CareReturnSafetyCheckRequired, "A recent safety check is needed before coming back.").With("next", "/care/return");
        }

        var tracked = await p.Db.CareClosures.FirstAsync(c => c.Id == closure.Id, ct).ConfigureAwait(false);
        tracked.RequestReturn(clock);
        var available = (await directory.AvailableAsync(p.Language, ct).ConfigureAwait(false)).Select(a => a.Therapist.Id).ToHashSet();
        if (available.Contains(closure.TherapistUserId))
        {
            p.Db.MatchSuggestions.RemoveRange(await p.Db.MatchSuggestions.Where(m => m.PatientId == p.PatientId).ToListAsync(ct).ConfigureAwait(false));
            p.Db.MatchSuggestions.Add(MatchSuggestion.Create(p.PatientId, closure.TherapistUserId, 1, [], clock));
        }

        audit.Append("care.return_requested", "CareClosure", closure.Id.ToString(), (await p.AccountAsync(ct).ConfigureAwait(false)).PseudonymId, $"screening={check.Id};level={(int)check.Level}");
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new ReturnResponse(available.Contains(closure.TherapistUserId) ? "/matches" : "/therapists"));
    }

    /// <summary>The latest closure a supervisor signed off, while the patient has no active therapist (404 otherwise).</summary>
    internal static async Task<CareClosure?> LatestSignedOffAsync(TamaneenDbContext db, Guid patientId, CancellationToken ct)
    {
        var closure = await db.CareClosures.AsNoTracking().Where(c => c.PatientId == patientId && c.Status == CareClosureStatus.SignedOff).OrderByDescending(c => c.DecidedAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (closure is null)
        {
            return null;
        }

        var laterCare = await db.CareAssignments.AsNoTracking().AnyAsync(a => a.PatientId == patientId && a.EndedAtUtc == null, ct).ConfigureAwait(false);
        return laterCare ? null : closure;
    }

    private static async Task<(CareClosure Closure, PatientClosureDto Dto)> SignedOffAsync(PatientContext p, CancellationToken ct)
    {
        var closure = await LatestSignedOffAsync(p.Db, p.PatientId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, "No signed-off end of care.");
        var plan = await p.DecryptJsonAsync<StaffCareEndpoints.StoredRelapsePlan>(Fields.ClosureRelapsePlan, closure.RelapsePlanCipher, ct).ConfigureAwait(false);
        var name = await p.Db.StaffProfiles.AsNoTracking().Where(s => s.UserId == closure.TherapistUserId).Select(s => p.Language == "en" ? s.DisplayNameEn : s.DisplayNameAr).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;
        return (closure, new PatientClosureDto(
            name,
            closure.SessionCount,
            await p.DecryptOptionalAsync(Fields.ClosurePatientSummary, closure.PatientSummaryCipher, ct).ConfigureAwait(false) ?? string.Empty,
            string.IsNullOrEmpty(plan?.WarningSigns) ? null : plan.WarningSigns,
            string.IsNullOrEmpty(plan?.WhatToDo) ? null : plan.WhatToDo,
            closure.FollowUp,
            closure.KeepLibrary,
            closure.DecidedAtUtc!.Value,
            closure.ReturnRequestedAtUtc is not null));
    }
}
