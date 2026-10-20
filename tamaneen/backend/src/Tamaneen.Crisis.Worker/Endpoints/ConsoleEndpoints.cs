using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Tamaneen.Crisis.Assessment;
using Tamaneen.Crisis.Gate;
using Tamaneen.Crisis.Worker.Escalation;
using Tamaneen.Crisis.Worker.Hubs;
using Tamaneen.Hosting;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Auth;
using Tamaneen.Infrastructure.Crisis;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.Infrastructure.Security;
using Tamaneen.SharedKernel;

namespace Tamaneen.Crisis.Worker.Endpoints;

/// <summary>
/// Actions of the crisis console (S04). Run as the signed-in clinician, so row-level security applies.
/// Patient words and contact data are shown only after a fresh authenticator code, for ten minutes, and
/// every showing is written to the audit ledger with its reason.
/// </summary>
public static class ConsoleEndpoints
{
    public static readonly TimeSpan RevealFor = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Same field names the API uses to encrypt these values (see Tamaneen.Api Patients/Fields).
    private const string SafetyInfoField = ProtectedFields.EmergencyContact;
    private const string ContactField = "identity.contact";
    private const string ActionTextField = "crisis.action_text";

    public sealed record CloseRequest(string? Outcome, string? Reason);

    public sealed record StepUpRequest(string? TotpCode, string? Reason);

    public sealed record ActionRequest(string? Kind, string? Text);

    public sealed record TimelineEntry(DateTimeOffset At, string Kind, string Text, string? Actor);

    /// <summary>The alert fields (same as CrisisAlertMessage, flat) plus what only the console detail shows.</summary>
    public sealed record CrisisEventDetail(
        Guid EventId,
        string Kind,
        string PseudonymId,
        int Level,
        string LevelLabel,
        string Title,
        string Body,
        string PatientLanguage,
        string PatientLanguageName,
        int EscalationTier,
        string EscalationTierLabel,
        string Status,
        DateTimeOffset OpenedAtUtc,
        DateTimeOffset AckDeadlineUtc,
        IReadOnlyList<string> Categories,
        string Dir,
        string Number,
        string Source,
        string SourceLabel,
        string? AcknowledgedBy,
        IReadOnlyList<string> RuleIds,
        string RulePackVersion,
        DateTimeOffset? ContactDeadlineUtc,
        bool HasContactPhone,
        bool HasTrustedContact,
        bool LocationConsent,
        string? MaskedPhone,
        IReadOnlyList<TimelineEntry> Timeline,
        bool CanClose);

    public sealed record ExcerptResponse(string Excerpt, DateTimeOffset HideAt);

    public sealed record RevealedContact(string? ContactPhone, string? TrustedName, string? TrustedPhone, bool LocationConsent, DateTimeOffset HideAt);

    public sealed record HealthResponse(string Gate, string Classifier, string Comms, string? OnCall, string? Backup);

    private sealed record SafetyInfoData(string? ContactPhone, string? TrustedName, string? TrustedPhone);

    public static IEndpointRouteBuilder MapConsoleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/crisis");
        group.MapGet("/events/open", ListOpenAsync).RequireAuthorization(AuthSetup.CrisisResponderPolicy);
        group.MapGet("/events/{id:guid}", DetailAsync).RequireAuthorization(AuthSetup.CrisisResponderPolicy);
        group.MapPost("/events/{id:guid}/acknowledge", AcknowledgeAsync).RequireAuthorization(AuthSetup.CrisisResponderPolicy);
        group.MapPost("/events/{id:guid}/excerpt", ExcerptAsync).RequireAuthorization(AuthSetup.CrisisResponderPolicy);
        group.MapPost("/events/{id:guid}/reveal-contact", RevealContactAsync).RequireAuthorization(AuthSetup.CrisisResponderPolicy);
        group.MapPost("/events/{id:guid}/actions", ActionAsync).RequireAuthorization(AuthSetup.CrisisResponderPolicy);
        group.MapPost("/events/{id:guid}/close", CloseAsync).RequireAuthorization(AuthSetup.CrisisCloserPolicy);
        group.MapGet("/health", HealthAsync).RequireAuthorization(AuthSetup.CrisisResponderPolicy);
        return app;
    }

    internal static async Task<Ok<List<CrisisAlertMessage>>> ListOpenAsync(HttpContext http, TamaneenDbContext db, CrisisAlertFactory factory, CancellationToken cancellationToken)
    {
        var language = LocalizationSetup.RequestLanguage(http);
        var events = await db.CrisisEvents.Where(e => e.Status != CrisisEventStatus.Closed)
            .OrderByDescending(e => e.Level).ThenBy(e => e.AckDeadlineUtc)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var names = await NamesAsync(db, events.Select(e => e.AcknowledgedByUserId), language, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(events.Select(e => factory.Create(e, null, language, e.AcknowledgedByUserId is { } a ? names.GetValueOrDefault(a) : null)).ToList());
    }

    internal static async Task<Ok<CrisisEventDetail>> DetailAsync(Guid id, HttpContext http, ICurrentUser user, TamaneenDbContext db, CrisisAlertFactory factory, IPatientDataProtector protector, IStringLocalizer<CrisisResource> text, IClock clock, CancellationToken ct)
    {
        var evt = await FindAsync(db, id, ct).ConfigureAwait(false);
        return TypedResults.Ok(await BuildDetailAsync(evt, LocalizationSetup.RequestLanguage(http), user, db, factory, protector, text, ct).ConfigureAwait(false));
    }

    internal static async Task<Ok<CrisisAlertMessage>> AcknowledgeAsync(
        Guid id,
        HttpContext http,
        ICurrentUser user,
        TamaneenDbContext db,
        IAuditLog audit,
        ICrisisAlertPublisher alerts,
        CrisisAlertFactory factory,
        IOptions<CrisisSlaOptions> sla,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var evt = await FindAsync(db, id, cancellationToken).ConfigureAwait(false);
        evt.Acknowledge(user.UserId!.Value, sla.Value.ContactWithin(evt.Level), clock);
        audit.Append("crisis.acknowledged", "CrisisEvent", evt.Id.ToString(), evt.PseudonymId);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two clinicians pressed "take the case" at the same moment: the first one wins.
            throw new DomainException(ErrorCodes.CrisisAlreadyAcknowledged, $"Crisis event {id} was acknowledged concurrently.");
        }

        await alerts.EventUpdatedAsync(evt, CrisisUpdateKind.Acknowledged, cancellationToken).ConfigureAwait(false);
        var language = LocalizationSetup.RequestLanguage(http);
        var names = await NamesAsync(db, [user.UserId], language, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(factory.Create(evt, CrisisUpdateKind.Acknowledged, language, names.GetValueOrDefault(user.UserId!.Value)));
    }

    internal static async Task<Ok<ExcerptResponse>> ExcerptAsync(Guid id, StepUpRequest request, ICurrentUser user, TamaneenDbContext db, StepUpVerifier stepUp, IPatientDataProtector protector, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        await stepUp.EnsureAsync(user.UserId!.Value, request.TotpCode, ct).ConfigureAwait(false);
        var evt = await FindAsync(db, id, ct).ConfigureAwait(false);
        var excerpt = evt.ExcerptCipher is null ? string.Empty : await protector.DecryptAsync(evt.PatientId, CrisisCaseStore.ExcerptField, evt.ExcerptCipher, ct).ConfigureAwait(false);
        audit.Append("crisis.excerpt_viewed", "CrisisEvent", evt.Id.ToString(), evt.PseudonymId, Reason(request.Reason));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new ExcerptResponse(excerpt, clock.UtcNow + RevealFor));
    }

    internal static async Task<Ok<RevealedContact>> RevealContactAsync(Guid id, StepUpRequest request, ICurrentUser user, TamaneenDbContext db, StepUpVerifier stepUp, IPatientDataProtector protector, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new DomainException(ErrorCodes.ReasonRequired, "A reason is required to reveal contact data.");
        }

        await stepUp.EnsureAsync(user.UserId!.Value, request.TotpCode, ct).ConfigureAwait(false);
        var evt = await FindAsync(db, id, ct).ConfigureAwait(false);
        var profile = await db.PatientProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.PatientId == evt.PatientId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, "Patient profile not visible.");
        var safety = profile.EmergencyContactCipher is null ? null
            : JsonSerializer.Deserialize<SafetyInfoData>(await protector.DecryptAsync(evt.PatientId, SafetyInfoField, profile.EmergencyContactCipher, ct).ConfigureAwait(false), Json);
        var phone = safety?.ContactPhone;
        if (phone is null && profile.ContactType == "phone" && profile.ContactCipher is not null)
        {
            phone = await protector.DecryptAsync(evt.PatientId, ContactField, profile.ContactCipher, ct).ConfigureAwait(false);
        }

        audit.Append("crisis.contact_revealed", "CrisisEvent", evt.Id.ToString(), evt.PseudonymId, Reason(request.Reason));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new RevealedContact(phone, safety?.TrustedName, safety?.TrustedPhone, profile.ShareLocation, clock.UtcNow + RevealFor));
    }

    internal static async Task<Ok<CrisisEventDetail>> ActionAsync(
        Guid id,
        ActionRequest request,
        HttpContext http,
        ICurrentUser user,
        TamaneenDbContext db,
        CrisisAlertFactory factory,
        IPatientDataProtector protector,
        ICrisisAlertPublisher alerts,
        IAuditLog audit,
        IStringLocalizer<CrisisResource> text,
        IOptions<CrisisSlaOptions> sla,
        IClock clock,
        CancellationToken ct)
    {
        var kind = request.Kind ?? string.Empty;
        if (!CrisisAction.Kinds.Contains(kind))
        {
            throw new DomainException(ErrorCodes.Validation, $"Unknown action {kind}.");
        }

        var note = request.Text?.Trim();
        if (note is { Length: > 2000 } || (kind is "note" or "local_emergency" && string.IsNullOrEmpty(note)))
        {
            throw new DomainException(ErrorCodes.Validation, "Action text missing or too long.");
        }

        var evt = await FindAsync(db, id, ct).ConfigureAwait(false);
        var cipher = string.IsNullOrEmpty(note) ? null : await protector.EncryptAsync(evt.PatientId, ActionTextField, note, ct).ConfigureAwait(false);
        db.CrisisActions.Add(CrisisAction.Record(evt, kind, user.UserId!.Value, user.Role!, cipher, clock));
        if (kind == "handover")
        {
            evt.HandOver(sla.Value.AcknowledgeWithin(evt.Level), clock);
        }

        audit.Append($"crisis.{kind}", "CrisisEvent", evt.Id.ToString(), evt.PseudonymId);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (kind == "handover")
        {
            await alerts.EventUpdatedAsync(evt, CrisisUpdateKind.HandedOver, ct).ConfigureAwait(false);
        }

        return TypedResults.Ok(await BuildDetailAsync(evt, LocalizationSetup.RequestLanguage(http), user, db, factory, protector, text, ct).ConfigureAwait(false));
    }

    internal static async Task<Ok<CrisisAlertMessage>> CloseAsync(
        Guid id,
        CloseRequest request,
        HttpContext http,
        ICurrentUser user,
        TamaneenDbContext db,
        IAuditLog audit,
        ICrisisAlertPublisher alerts,
        CrisisAlertFactory factory,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var evt = await FindAsync(db, id, cancellationToken).ConfigureAwait(false);
        if (request.Outcome is null)
        {
            evt.Close(user.UserId!.Value, user.Role!, request.Reason ?? string.Empty, clock);
        }
        else
        {
            evt.Close(user.UserId!.Value, user.Role!, request.Outcome, request.Reason ?? string.Empty, clock);
        }

        // Closing is the only way a locked chat reopens.
        var locks = await db.ChatLocks.Where(l => l.CrisisEventId == evt.Id && l.ReleasedAtUtc == null).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var chatLock in locks)
        {
            chatLock.Release(user.UserId!.Value, clock);
        }

        audit.Append("crisis.closed", "CrisisEvent", evt.Id.ToString(), evt.PseudonymId, evt.CloseOutcome is null ? evt.CloseReason : $"{evt.CloseOutcome}: {evt.CloseReason}");
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await alerts.EventUpdatedAsync(evt, CrisisUpdateKind.Closed, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(factory.Create(evt, CrisisUpdateKind.Closed, LocalizationSetup.RequestLanguage(http)));
    }

    /// <summary>
    /// Status chips at the top of the console: the gate answers (this process), the rule engine is loaded,
    /// and pages are going out (no page waiting longer than two minutes, loops alive).
    /// </summary>
    internal static async Task<Ok<HealthResponse>> HealthAsync(HttpContext http, TamaneenDbContext db, IRiskEvaluator evaluator, LoopsHealthCheck loops, IClock clock, CancellationToken ct)
    {
        var language = LocalizationSetup.RequestLanguage(http);
        var classifier = string.IsNullOrEmpty(evaluator.Version) ? "down" : "ok";
        var loopState = await loops.CheckHealthAsync(new HealthCheckContext(), ct).ConfigureAwait(false);
        var overdue = clock.UtcNow.AddMinutes(-2);
        var stuckPages = await db.PageRequests.AsNoTracking().AnyAsync(p => p.SentAtUtc == null && p.CreatedAtUtc < overdue, ct).ConfigureAwait(false);
        var comms = loopState.Status != HealthStatus.Healthy ? "down" : stuckPages ? "degraded" : "ok";
        var rota = await db.StaffProfiles.AsNoTracking().Where(s => s.OnCallTier != null).OrderBy(s => s.OnCallTier).Take(2).ToListAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new HealthResponse("ok", classifier, comms, rota.FirstOrDefault(r => r.OnCallTier == 0)?.DisplayName(language), rota.FirstOrDefault(r => r.OnCallTier == 1)?.DisplayName(language)));
    }

    private static async Task<CrisisEventDetail> BuildDetailAsync(CrisisEvent evt, string language, ICurrentUser user, TamaneenDbContext db, CrisisAlertFactory factory, IPatientDataProtector protector, IStringLocalizer<CrisisResource> text, CancellationToken ct)
    {
        var profile = await db.PatientProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.PatientId == evt.PatientId, ct).ConfigureAwait(false);
        var hasSafety = profile?.EmergencyContactCipher is not null;
        SafetyInfoData? safety = null;
        if (hasSafety)
        {
            safety = JsonSerializer.Deserialize<SafetyInfoData>(await protector.DecryptAsync(evt.PatientId, SafetyInfoField, profile!.EmergencyContactCipher!, ct).ConfigureAwait(false), Json);
        }

        var phone = safety?.ContactPhone;
        if (phone is null && profile is { ContactType: "phone", ContactCipher: { } contactCipher })
        {
            phone = await protector.DecryptAsync(evt.PatientId, ContactField, contactCipher, ct).ConfigureAwait(false);
        }

        // Only the last two digits are shown before verification ("••• ••• ••21").
        var digits = phone is null ? null : new string(phone.Where(char.IsDigit).ToArray());
        var masked = digits is { Length: >= 2 } ? $"••• ••• ••{digits[^2..]}" : null;

        var actions = await db.CrisisActions.AsNoTracking().Where(a => a.CrisisEventId == evt.Id).ToListAsync(ct).ConfigureAwait(false);
        var pages = await db.PageRequests.AsNoTracking().Where(p => p.CrisisEventId == evt.Id).ToListAsync(ct).ConfigureAwait(false);
        var audits = await db.AuditEntries.AsNoTracking().Where(a => a.EntityType == "CrisisEvent" && a.EntityId == evt.Id.ToString()
            && (a.Action == "crisis.excerpt_viewed" || a.Action == "crisis.contact_revealed")).ToListAsync(ct).ConfigureAwait(false);
        var actorIds = actions.Select(a => (Guid?)a.ActorUserId).Concat(audits.Select(a => a.ActorUserId)).Append(evt.AcknowledgedByUserId).Append(evt.ClosedByUserId);
        var names = await NamesAsync(db, actorIds, language, ct).ConfigureAwait(false);
        string? Name(Guid? userId) => userId is { } u ? names.GetValueOrDefault(u) : null;

        var timeline = LocalizationSetup.WithLanguage(language, () =>
        {
            var entries = new List<TimelineEntry> { new(evt.OpenedAtUtc, "opened", text["timeline.opened", factory.SourceLabel(evt.Source)].Value, null) };
            entries.AddRange(pages.Where(p => p.SentAtUtc is not null).Select(p => new TimelineEntry(p.SentAtUtc!.Value, "paged", text["timeline.paged", text[$"tier.{Math.Min((int)p.Tier, 2)}"].Value].Value, null)));
            if (evt.AcknowledgedAtUtc is { } ackAt)
            {
                entries.Add(new TimelineEntry(ackAt, "acknowledged", text["timeline.acknowledged"].Value, Name(evt.AcknowledgedByUserId)));
            }

            entries.AddRange(actions.Select(a => new TimelineEntry(a.OccurredAtUtc, a.Kind, text[$"timeline.{a.Kind}"].Value, Name(a.ActorUserId))));
            entries.AddRange(audits.Select(a => new TimelineEntry(a.OccurredAtUtc, a.Action == "crisis.excerpt_viewed" ? "excerpt_viewed" : "contact_revealed", text[$"timeline.{(a.Action == "crisis.excerpt_viewed" ? "excerpt_viewed" : "contact_revealed")}"].Value, Name(a.ActorUserId))));
            if (evt.ClosedAtUtc is { } closedAt)
            {
                entries.Add(new TimelineEntry(closedAt, "closed", text["timeline.closed"].Value, Name(evt.ClosedByUserId)));
            }

            return entries.OrderBy(e => e.At).ToList();
        });

        // Notes are shown in the timeline text only to people who can see the event (row-level security already applied).
        for (var i = 0; i < timeline.Count; i++)
        {
            var action = actions.FirstOrDefault(a => a.OccurredAtUtc == timeline[i].At && a.Kind == timeline[i].Kind && a.TextCipher is not null);
            if (action is not null)
            {
                var note = await protector.DecryptAsync(evt.PatientId, ActionTextField, action.TextCipher!, ct).ConfigureAwait(false);
                timeline[i] = timeline[i] with { Text = $"{timeline[i].Text}: {note}" };
            }
        }

        var a = factory.Create(evt, null, language, Name(evt.AcknowledgedByUserId));
        return new CrisisEventDetail(
            a.EventId, a.Kind, a.PseudonymId, a.Level, a.LevelLabel, a.Title, a.Body, a.PatientLanguage, a.PatientLanguageName,
            a.EscalationTier, a.EscalationTierLabel, a.Status, a.OpenedAtUtc, a.AckDeadlineUtc, a.Categories, a.Dir, a.Number, a.Source, a.SourceLabel, a.AcknowledgedBy,
            evt.TriggerRuleIds.Split(',', StringSplitOptions.RemoveEmptyEntries),
            evt.RulePackVersion,
            evt.ContactDeadlineUtc,
            phone is not null,
            safety?.TrustedName is not null || safety?.TrustedPhone is not null,
            profile?.ShareLocation ?? false,
            masked,
            timeline,
            user.Role is AppRoles.Supervisor or AppRoles.ClinicalLead && evt.Status != CrisisEventStatus.Closed);
    }

    private static async Task<Dictionary<Guid, string>> NamesAsync(TamaneenDbContext db, IEnumerable<Guid?> userIds, string language, CancellationToken ct)
    {
        var ids = userIds.Where(u => u is not null).Select(u => u!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var profiles = await db.StaffProfiles.AsNoTracking().Where(s => ids.Contains(s.UserId)).ToListAsync(ct).ConfigureAwait(false);
        return profiles.ToDictionary(p => p.UserId, p => p.DisplayName(language));
    }

    private static async Task<CrisisEvent> FindAsync(TamaneenDbContext db, Guid id, CancellationToken ct) =>
        await db.CrisisEvents.FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Crisis event {id} is not visible to this user.");

    private static string? Reason(string? reason) => string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()[..Math.Min(reason.Trim().Length, 500)];
}
