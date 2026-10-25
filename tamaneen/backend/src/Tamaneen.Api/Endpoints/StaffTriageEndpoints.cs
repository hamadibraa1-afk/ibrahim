using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Tamaneen.Api.Clinical;
using Tamaneen.Api.Gate;
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
/// S03, triage review (supervisors and the clinical lead). A supervisor claims a case for 15 minutes (server time) and
/// decides with a reason: approve the suggestions, override the risk, change the suggestions, escalate to a crisis or
/// close with a referral. The final level is never below an open or recent crisis, and lowering from High or Critical
/// needs the clinical lead (EC-48). Every read of a case and every decision is audited.
/// </summary>
public static class StaffTriageEndpoints
{
    private static readonly string[] SummaryKeys = ["concern", "goals", "supports", "priorCare"];

    public sealed record ClaimDto(string? By, DateTimeOffset? Until, bool Mine);

    public sealed record PendingCoSignDto(int RiskLevel, string? RequestedBy, DateTimeOffset RequestedAt);

    public sealed record TriageQueueItemDto(
        Guid Id,
        string PseudonymId,
        string Path,
        bool ContactRequested,
        int RiskLevel,
        int? Phq9,
        int? Gad7,
        bool? CssrsPositive,
        DateTimeOffset SubmittedAt,
        DateTimeOffset DueAt,
        ClaimDto Claim,
        int? PendingRiskLevel);

    public sealed record SuggestedTherapistDto(Guid Id, int Rank, string DisplayName, IReadOnlyList<string> Specialties, IReadOnlyList<string> Shared, IReadOnlyList<string> Languages, int Load, int Capacity, DateTimeOffset? NextAvailableAt);

    public sealed record TriageCaseDto(
        Guid Id,
        string PseudonymId,
        string Path,
        bool ContactRequested,
        string Status,
        int RiskLevel,
        int RuleRiskLevel,
        IReadOnlyList<string> RuleReasons,
        int? AiRiskLevel,
        int CrisisFloor,
        int? Phq9,
        int? Gad7,
        bool? CssrsPositive,
        int? Phq9Item9,
        string Language,
        string? CarePath,
        string Summary,
        IReadOnlyList<SuggestedTherapistDto> Suggestions,
        bool SuggestionsSaved,
        ClaimDto Claim,
        PendingCoSignDto? PendingCoSign,
        bool CanCoSign,
        DateTimeOffset SubmittedAt,
        DateTimeOffset DueAt,
        DateTimeOffset ServerNow);

    public sealed record DecisionRequest(string? Kind, string? Reason, int? RiskLevel, List<Guid>? TherapistIds, List<string>? Focus, List<string>? Referrals, bool? Approve);

    public sealed record DecisionResult(string Kind, string Status, int RiskLevel, bool CoSignRequested, Guid? CrisisEventId);

    public static IEndpointRouteBuilder MapStaffTriageEndpoints(this IEndpointRouteBuilder app)
    {
        var supervisors = app.MapGroup("/v1/staff/triage").RequireAuthorization(AuthSetup.CrisisCloserPolicy);
        supervisors.MapGet("/{id:guid}", DetailAsync);
        supervisors.MapGet("/{id:guid}/therapists", TherapistsAsync);
        supervisors.MapPost("/{id:guid}/claim", ClaimAsync);
        supervisors.MapDelete("/{id:guid}/claim", ReleaseAsync);
        supervisors.MapPost("/{id:guid}/decisions", DecideAsync);
        return app;
    }

    /// <summary>Open cases, highest risk first, then the soonest deadline (GET /v1/staff/triage).</summary>
    internal static async Task<Ok<List<TriageQueueItemDto>>> QueueAsync(ICurrentUser user, TamaneenDbContext db, IClock clock, CancellationToken ct)
    {
        var cases = await db.TriageCases.AsNoTracking().Where(t => t.Status == TriageStatus.Pending).OrderByDescending(t => t.RiskLevel).ThenBy(t => t.DueAtUtc).Take(200).ToListAsync(ct).ConfigureAwait(false);
        var ids = cases.Select(c => c.PatientId).ToList();
        var pseudonyms = await db.UserAccounts.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.PseudonymId, ct).ConfigureAwait(false);
        var contact = await db.CareRequests.AsNoTracking().Where(r => ids.Contains(r.PatientId) && r.HandledAtUtc == null).Select(r => r.PatientId).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var names = await NamesAsync(db, cases.Select(c => c.ClaimedByUserId), user.Language, ct).ConfigureAwait(false);
        var now = clock.UtcNow;
        return TypedResults.Ok(cases.Select(c => new TriageQueueItemDto(
            c.Id, pseudonyms[c.PatientId], c.Path, contact.Contains(c.PatientId), (int)c.RiskLevel, c.Phq9, c.Gad7, c.CssrsPositive, c.SubmittedAtUtc, c.DueAtUtc,
            Claim(c, user, names, now), (int?)c.PendingRiskLevel)).ToList());
    }

    /// <summary>The case (SVC-36): levels and why, safety indicators, the patient's summary and the proposed therapists. Audited.</summary>
    internal static async Task<Ok<TriageCaseDto>> DetailAsync(Guid id, ICurrentUser user, TamaneenDbContext db, TherapistDirectory directory, IPatientDataProtector protector, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var triage = await FindAsync(db, id, ct).ConfigureAwait(false);
        var pseudonym = await PseudonymAsync(db, triage.PatientId, ct).ConfigureAwait(false);
        audit.Append("triage.case_opened", "TriageCase", triage.Id.ToString(), pseudonym);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(await BuildAsync(triage, pseudonym, user, db, directory, protector, clock, ct).ConfigureAwait(false));
    }

    /// <summary>Approved therapists with free capacity, for "change the suggestions" (SB-34).</summary>
    internal static async Task<Ok<List<SuggestedTherapistDto>>> TherapistsAsync(Guid id, ICurrentUser user, TamaneenDbContext db, TherapistDirectory directory, CancellationToken ct)
    {
        await FindAsync(db, id, ct).ConfigureAwait(false);
        var available = await directory.AvailableAsync(user.Language, ct).ConfigureAwait(false);
        return TypedResults.Ok(available.Select((a, i) => Card(a.Therapist, i + 1, [], a.Load, a.Capacity)).ToList());
    }

    /// <summary>SB-30: claim for 15 minutes; renewing your own claim extends it. 409 names who holds it, and until when.</summary>
    internal static async Task<Ok<TriageCaseDto>> ClaimAsync(Guid id, ICurrentUser user, TamaneenDbContext db, TherapistDirectory directory, IPatientDataProtector protector, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var triage = await FindAsync(db, id, ct).ConfigureAwait(false);
        try
        {
            triage.Claim(user.UserId!.Value, clock);
            var pseudonym = await PseudonymAsync(db, triage.PatientId, ct).ConfigureAwait(false);
            audit.Append("triage.claimed", "TriageCase", triage.Id.ToString(), pseudonym);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return TypedResults.Ok(await BuildAsync(triage, pseudonym, user, db, directory, protector, clock, ct).ConfigureAwait(false));
        }
        catch (DomainException ex) when (ex.Code == ErrorCodes.TriageClaimedByOther)
        {
            throw await WithHolderAsync(ex, triage, db, user.Language, ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two supervisors claimed at the same moment: the first one wins.
            db.ChangeTracker.Clear();
            var latest = await FindAsync(db, id, ct).ConfigureAwait(false);
            throw await WithHolderAsync(new DomainException(ErrorCodes.TriageClaimedByOther, $"Triage {id} was claimed concurrently."), latest, db, user.Language, ct).ConfigureAwait(false);
        }
    }

    /// <summary>SB-31: release the caller's claim (also sent when the page is left).</summary>
    internal static async Task<NoContent> ReleaseAsync(Guid id, ICurrentUser user, TamaneenDbContext db, IAuditLog audit, CancellationToken ct)
    {
        var triage = await FindAsync(db, id, ct).ConfigureAwait(false);
        if (triage.ClaimedByUserId == user.UserId)
        {
            triage.Release(user.UserId!.Value);
            audit.Append("triage.released", "TriageCase", triage.Id.ToString(), await PseudonymAsync(db, triage.PatientId, ct).ConfigureAwait(false));
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Someone decided or reclaimed meanwhile; nothing of ours is left to release.
            }
        }

        return TypedResults.NoContent();
    }

    /// <summary>SVC-37: one decision on a claimed case. The reason is required for everything but Approve and goes to the audit log.</summary>
    internal static async Task<Ok<DecisionResult>> DecideAsync(Guid id, DecisionRequest request, ICurrentUser user, TamaneenDbContext db, TherapistDirectory directory, CrisisGateClient gate, IPatientDataProtector protector, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var triage = await FindAsync(db, id, ct).ConfigureAwait(false);
        var me = user.UserId!.Value;
        var reason = request.Reason?.Trim();
        var patient = await db.UserAccounts.AsNoTracking().FirstAsync(a => a.Id == triage.PatientId, ct).ConfigureAwait(false);
        var floor = await StaffFileEndpoints.CrisisFloorAsync(db, triage.PatientId, clock, ct).ConfigureAwait(false);
        var before = triage.RiskLevel;
        var coSignRequested = false;
        Guid? eventId = null;
        var kind = request.Kind ?? string.Empty;
        var focus = (request.Focus ?? []).Where(TherapistProfile.SpecialtyCodes.Contains).Distinct().ToList();
        try
        {
            switch (kind)
            {
                case "approve":
                    await ReplaceSuggestionsAsync(db, directory, triage, patient.PreferredLanguage, request.TherapistIds, focus, keepExisting: true, clock, ct).ConfigureAwait(false);
                    triage.Approve(me, clock);
                    break;
                case "override_risk":
                    if (request.RiskLevel is not { } level)
                    {
                        throw MeEndpoints.Invalid("riskLevel");
                    }

                    coSignRequested = triage.OverrideRisk(me, user.Role == AppRoles.ClinicalLead, (RiskLevel)level, floor, reason, clock) == RiskChange.CoSignRequested;
                    break;
                case "reassign":
                    triage.EnsureCanReassign(me, reason, clock);
                    if (request.TherapistIds is not { Count: > 0 and <= 3 })
                    {
                        throw MeEndpoints.Invalid("therapistIds");
                    }

                    await ReplaceSuggestionsAsync(db, directory, triage, patient.PreferredLanguage, request.TherapistIds, focus, keepExisting: false, clock, ct).ConfigureAwait(false);
                    break;
                case "escalate":
                    var crisisLevel = request.RiskLevel is (int)RiskLevel.Critical ? RiskLevel.Critical : (RiskLevel)Math.Max((int)RiskLevel.High, (int)triage.RiskLevel);
                    triage.Escalate(me, crisisLevel, reason, clock);
                    var verdict = await gate.RaiseManualAsync(triage.PatientId, patient.PreferredLanguage, crisisLevel, ct).ConfigureAwait(false);
                    if (verdict.Unavailable || verdict.CrisisEventId is null)
                    {
                        throw new DomainException(ErrorCodes.CrisisServiceUnavailable, "Crisis worker unavailable for an escalation.");
                    }

                    eventId = verdict.CrisisEventId;
                    var evt = await db.CrisisEvents.FirstOrDefaultAsync(e => e.Id == eventId, ct).ConfigureAwait(false);
                    if (evt is not null)
                    {
                        db.CrisisActions.Add(Tamaneen.Crisis.CrisisAction.Record(evt, "note", me, user.Role!, await protector.EncryptAsync(triage.PatientId, Fields.CrisisNote, reason!, ct).ConfigureAwait(false), clock));
                    }

                    break;
                case "close":
                    triage.Close(me, request.Referrals ?? [], reason, clock);
                    break;
                case "cosign":
                    if (user.Role != AppRoles.ClinicalLead)
                    {
                        throw new DomainException(ErrorCodes.TriageCoSignNotAllowed, "Only the clinical lead co-signs.");
                    }

                    if (string.IsNullOrWhiteSpace(reason))
                    {
                        throw new DomainException(ErrorCodes.ReasonRequired, "A reason is required to co-sign.");
                    }

                    triage.CoSign(me, request.Approve ?? false, floor);
                    break;
                default:
                    throw MeEndpoints.Invalid("kind");
            }
        }
        catch (DomainException ex) when (ex.Code == ErrorCodes.TriageClaimedByOther)
        {
            throw await WithHolderAsync(ex, triage, db, user.Language, ct).ConfigureAwait(false);
        }

        var detail = kind switch
        {
            "override_risk" => $"{(int)before}->{request.RiskLevel}{(coSignRequested ? " (co-sign requested)" : string.Empty)}",
            "cosign" => request.Approve == true ? $"approved {(int)before}->{(int)triage.RiskLevel}" : "refused",
            "close" => $"referrals={triage.Referrals}",
            "escalate" => $"event={eventId}",
            _ => $"risk={(int)triage.RiskLevel}",
        };
        audit.Append(kind == "approve" ? "triage.reviewed" : $"triage.{kind}", "TriageCase", triage.Id.ToString(), patient.PseudonymId,
            StaffFileEndpoints.Truncate(string.IsNullOrEmpty(reason) ? detail : $"{detail}: {reason}"));
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException(ErrorCodes.TriageClaimRequired, $"Triage {id} changed concurrently.");
        }

        return TypedResults.Ok(new DecisionResult(kind, MeEndpoints.TriageStatusCode(triage.Status), (int)triage.RiskLevel, coSignRequested, eventId));
    }

    // ---------- helpers ----------

    /// <summary>
    /// Saves the suggestions the patient will see: the therapists the supervisor chose (SB-34), or for Approve the ones
    /// already saved or, when there are none, the directory's best three. Only approved therapists with free capacity.
    /// </summary>
    private static async Task ReplaceSuggestionsAsync(TamaneenDbContext db, TherapistDirectory directory, TriageCase triage, string language, List<Guid>? chosen, IReadOnlyCollection<string> focus, bool keepExisting, IClock clock, CancellationToken ct)
    {
        var existing = await db.MatchSuggestions.Where(m => m.PatientId == triage.PatientId).ToListAsync(ct).ConfigureAwait(false);
        if (keepExisting && chosen is not { Count: > 0 } && existing.Any(m => !m.Dismissed))
        {
            return;
        }

        var available = (await directory.AvailableAsync(language, ct).ConfigureAwait(false)).ToDictionary(a => a.Therapist.Id);
        List<(Guid Id, IReadOnlyList<string> Shared)> picks;
        if (chosen is { Count: > 0 })
        {
            if (chosen.Count > 3 || chosen.Any(c => !available.ContainsKey(c)))
            {
                throw new DomainException(ErrorCodes.NoTherapistAvailable, "A chosen therapist is not available.");
            }

            picks = chosen.Distinct().Select(c => (c, (IReadOnlyList<string>)available[c].Therapist.Specialties.Where(focus.Contains).ToList())).ToList();
        }
        else
        {
            picks = (await directory.SuggestAsync(focus, language, 3, ct).ConfigureAwait(false)).Where(p => available.ContainsKey(p.TherapistId)).Select(p => (p.TherapistId, p.Shared)).ToList();
        }

        if (picks.Count == 0)
        {
            throw new DomainException(ErrorCodes.NoTherapistAvailable, "No therapist is available.");
        }

        db.MatchSuggestions.RemoveRange(existing);
        var rank = 1;
        foreach (var (therapistId, shared) in picks)
        {
            db.MatchSuggestions.Add(MatchSuggestion.Create(triage.PatientId, therapistId, rank++, shared, clock));
        }
    }

    private static async Task<TriageCaseDto> BuildAsync(TriageCase triage, string pseudonym, ICurrentUser user, TamaneenDbContext db, TherapistDirectory directory, IPatientDataProtector protector, IClock clock, CancellationToken ct)
    {
        var language = user.Language;
        var patient = await db.UserAccounts.AsNoTracking().FirstAsync(a => a.Id == triage.PatientId, ct).ConfigureAwait(false);
        var profile = await db.PatientProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.PatientId == triage.PatientId, ct).ConfigureAwait(false);
        var intake = triage.IntakeId is { } iid ? await db.IntakeSessions.AsNoTracking().FirstOrDefaultAsync(i => i.Id == iid, ct).ConfigureAwait(false) : null;
        var summary = string.Empty;
        if (intake?.SummaryCipher is { } cipher)
        {
            var stored = JsonSerializer.Deserialize<IntakeEndpoints.StoredSummary>(await protector.DecryptAsync(triage.PatientId, Fields.IntakeSummary, cipher, ct).ConfigureAwait(false), PatientContext.Json);
            summary = string.Join(" ", SummaryKeys.Select(k => stored?.Sections.GetValueOrDefault(k)?.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
        }

        var floor = await StaffFileEndpoints.CrisisFloorAsync(db, triage.PatientId, clock, ct).ConfigureAwait(false);
        var contact = await db.CareRequests.AsNoTracking().AnyAsync(r => r.PatientId == triage.PatientId && r.HandledAtUtc == null, ct).ConfigureAwait(false);
        var hadCrisis = await db.CrisisEvents.AsNoTracking().AnyAsync(e => e.PatientId == triage.PatientId && e.OpenedAtUtc <= triage.SubmittedAtUtc, ct).ConfigureAwait(false);
        var reasons = new List<string>();
        if (triage.Phq9 >= 15)
        {
            reasons.Add("phq9");
        }

        if (triage.Gad7 >= 15)
        {
            reasons.Add("gad7");
        }

        if (triage.CssrsPositive == true)
        {
            reasons.Add("cssrs");
        }

        if (hadCrisis)
        {
            reasons.Add("crisis");
        }

        if (reasons.Count == 0)
        {
            reasons.Add("routine");
        }

        // The suggestions the patient would get: saved ones (after a Reassign), else the directory's proposal.
        var available = (await directory.AvailableAsync(patient.PreferredLanguage, ct).ConfigureAwait(false)).ToDictionary(a => a.Therapist.Id);
        var saved = await db.MatchSuggestions.AsNoTracking().Where(m => m.PatientId == triage.PatientId && !m.Dismissed).OrderBy(m => m.Rank).ToListAsync(ct).ConfigureAwait(false);
        List<SuggestedTherapistDto> suggestions;
        if (saved.Count > 0)
        {
            var cards = (await directory.ListAsync(language, saved.Select(s => s.TherapistUserId), ct).ConfigureAwait(false)).ToDictionary(t => t.Id);
            suggestions = saved.Where(s => cards.ContainsKey(s.TherapistUserId)).Select(s =>
            {
                available.TryGetValue(s.TherapistUserId, out var a);
                return Card(cards[s.TherapistUserId], s.Rank, s.SharedSpecialties.Split(',', StringSplitOptions.RemoveEmptyEntries), a.Load, a.Capacity);
            }).ToList();
        }
        else
        {
            var picks = await directory.SuggestAsync([], patient.PreferredLanguage, 3, ct).ConfigureAwait(false);
            var cards = (await directory.ListAsync(language, picks.Select(p => p.TherapistId), ct).ConfigureAwait(false)).ToDictionary(t => t.Id);
            suggestions = picks.Where(p => cards.ContainsKey(p.TherapistId) && available.ContainsKey(p.TherapistId))
                .Select((p, i) => Card(cards[p.TherapistId], i + 1, p.Shared, available[p.TherapistId].Load, available[p.TherapistId].Capacity)).ToList();
        }

        var names = await NamesAsync(db, [triage.ClaimedByUserId, triage.PendingRiskRequestedBy], language, ct).ConfigureAwait(false);
        var now = clock.UtcNow;
        var item9 = intake is not null && intake.Scores("PHQ-9").TryGetValue(9, out var nine) ? nine : (int?)null;
        return new TriageCaseDto(
            triage.Id,
            pseudonym,
            triage.Path,
            contact,
            MeEndpoints.TriageStatusCode(triage.Status),
            (int)triage.RiskLevel,
            (int)(triage.RuleRiskLevel == RiskLevel.None ? triage.RiskLevel : triage.RuleRiskLevel),
            reasons,
            null,
            (int)floor,
            triage.Phq9,
            triage.Gad7,
            triage.CssrsPositive ?? await db.Screenings.AsNoTracking().Where(s => s.PatientId == triage.PatientId).OrderByDescending(s => s.CompletedAtUtc).Select(s => (bool?)s.Positive).FirstOrDefaultAsync(ct).ConfigureAwait(false),
            item9,
            patient.PreferredLanguage,
            profile?.CarePath,
            summary,
            suggestions,
            saved.Count > 0,
            Claim(triage, user, names, now),
            triage.PendingRiskLevel is { } pending ? new PendingCoSignDto((int)pending, triage.PendingRiskRequestedBy is { } by ? names.GetValueOrDefault(by) : null, triage.PendingRiskRequestedAtUtc!.Value) : null,
            user.Role == AppRoles.ClinicalLead && triage.PendingRiskLevel is not null && triage.PendingRiskRequestedBy != user.UserId,
            triage.SubmittedAtUtc,
            triage.DueAtUtc,
            now);
    }

    private static SuggestedTherapistDto Card(TherapistSummaryDto t, int rank, IReadOnlyList<string> shared, int load, int capacity) =>
        new(t.Id, rank, t.DisplayName, t.Specialties, shared, t.Languages, load, capacity, t.NextAvailableAt);

    private static ClaimDto Claim(TriageCase c, ICurrentUser user, IReadOnlyDictionary<Guid, string> names, DateTimeOffset now) =>
        c.ClaimedByUserId is { } holder && c.ClaimedUntilUtc > now
            ? new ClaimDto(names.GetValueOrDefault(holder), c.ClaimedUntilUtc, holder == user.UserId)
            : new ClaimDto(null, null, false);

    private static async Task<DomainException> WithHolderAsync(DomainException ex, TriageCase triage, TamaneenDbContext db, string language, CancellationToken ct)
    {
        var names = await NamesAsync(db, [triage.ClaimedByUserId], language, ct).ConfigureAwait(false);
        if (triage.ClaimedByUserId is { } holder)
        {
            ex.With("claimedBy", names.GetValueOrDefault(holder) ?? string.Empty);
        }

        if (triage.ClaimedUntilUtc is { } until)
        {
            ex.With("claimedUntil", until);
        }

        return ex;
    }

    private static async Task<Dictionary<Guid, string>> NamesAsync(TamaneenDbContext db, IEnumerable<Guid?> ids, string language, CancellationToken ct)
    {
        var list = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0)
        {
            return [];
        }

        return await db.StaffProfiles.AsNoTracking().Where(s => list.Contains(s.UserId))
            .ToDictionaryAsync(s => s.UserId, s => language == "en" ? s.DisplayNameEn : s.DisplayNameAr, ct).ConfigureAwait(false);
    }

    private static async Task<TriageCase> FindAsync(TamaneenDbContext db, Guid id, CancellationToken ct) =>
        await db.TriageCases.FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Triage {id} not found.");

    private static Task<string> PseudonymAsync(TamaneenDbContext db, Guid patientId, CancellationToken ct) =>
        db.UserAccounts.AsNoTracking().Where(a => a.Id == patientId).Select(a => a.PseudonymId).FirstAsync(ct);
}
