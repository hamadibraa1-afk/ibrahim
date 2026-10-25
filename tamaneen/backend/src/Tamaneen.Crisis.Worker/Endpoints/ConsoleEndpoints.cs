using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Tamaneen.Crisis.Assessment;
using Tamaneen.Crisis.Gate;
using Tamaneen.Crisis.Rules;
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

    /// <summary>Three call attempts within this window without reaching the patient bring up the next step (EC-33).</summary>
    public static readonly TimeSpan CallAttemptWindow = TimeSpan.FromMinutes(15);

    public const int CallAttemptsBeforeAdvice = 3;

    public sealed record ActionRequest(string? Kind, string? Text, string? Outcome);

    public sealed record HandoverRequest(Guid? ToUserId, string? Note);

    public sealed record TimelineEntry(DateTimeOffset At, string Kind, string Text, string? Actor);

    /// <summary>A handover offered and not yet accepted (SB-43). ForMe: the signed-in user is the one asked.</summary>
    public sealed record PendingHandover(Guid ToUserId, string? ToName, string? FromName, DateTimeOffset RequestedAtUtc, bool ForMe);

    /// <summary>A supervisor's "unreachable" close waiting for the clinical lead (SB-44).</summary>
    public sealed record PendingClose(string Outcome, string Reason, string? ByName, DateTimeOffset RequestedAtUtc);

    /// <summary>A colleague who can take a handover: supervisors, the clinical lead and on-call clinicians.</summary>
    public sealed record Responder(Guid UserId, string DisplayName, string Role, bool OnCall);

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
        bool CanClose,
        bool CanAct,
        bool IsOwner,
        PendingHandover? PendingHandover,
        PendingClose? PendingClose,
        bool CanCoSign,
        string EmergencyNumber,
        string EmergencyScript,
        int RecentCallAttempts,
        string? CallAdvice,
        DateTimeOffset? ReviewDueAtUtc);

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
        group.MapPost("/events/{id:guid}/handover", HandoverAsync).RequireAuthorization(AuthSetup.CrisisResponderPolicy);
        group.MapPost("/events/{id:guid}/handover/accept", AcceptHandoverAsync).RequireAuthorization(AuthSetup.CrisisResponderPolicy);
        group.MapPost("/events/{id:guid}/handover/decline", DeclineHandoverAsync).RequireAuthorization(AuthSetup.CrisisResponderPolicy);
        group.MapPost("/events/{id:guid}/close", CloseAsync).RequireAuthorization(AuthSetup.CrisisCloserPolicy);
        group.MapPost("/events/{id:guid}/close/cosign", CoSignCloseAsync).RequireAuthorization(AuthSetup.CrisisCloserPolicy);
        group.MapGet("/responders", RespondersAsync).RequireAuthorization(AuthSetup.CrisisResponderPolicy);
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
        return TypedResults.Ok(await BuildDetailAsync(evt, http, user, db, factory, protector, text, ct).ConfigureAwait(false));
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
        if (!CrisisAction.RequestKinds.Contains(kind))
        {
            throw new DomainException(ErrorCodes.Validation, $"Unknown action {kind}.");
        }

        var note = request.Text?.Trim();
        if (note is { Length: > 2000 } || (kind is "note" or "local_emergency" or "contacted" or "refer" && string.IsNullOrEmpty(note)))
        {
            throw new DomainException(ErrorCodes.Validation, "Action text missing or too long.");
        }

        var evt = await FindAsync(db, id, ct).ConfigureAwait(false);

        // The patient's therapist may always leave the case with the on-call (S01) without taking it; every other
        // action is for the owner, a supervisor or the clinical lead once someone took the case (EC-35).
        if (kind != "refer")
        {
            evt.EnsureCanAct(user.UserId!.Value, user.Role);
        }

        var cipher = string.IsNullOrEmpty(note) ? null : await protector.EncryptAsync(evt.PatientId, ActionTextField, note, ct).ConfigureAwait(false);
        db.CrisisActions.Add(CrisisAction.Record(evt, kind, user.UserId!.Value, user.Role!, cipher, kind == "call_outcome" ? request.Outcome : null, null, clock));
        CrisisUpdateKind? update = null;
        switch (kind)
        {
            case "handover":
                evt.HandOver(sla.Value.AcknowledgeWithin(evt.Level), clock);
                update = CrisisUpdateKind.HandedOver;
                break;
            case "contacted":
                evt.MarkInContact();
                update = CrisisUpdateKind.Updated;
                break;
        }

        audit.Append($"crisis.{kind}", "CrisisEvent", evt.Id.ToString(), evt.PseudonymId, kind == "call_outcome" ? request.Outcome : null);
        await SaveAsync(db, evt.Id, ct).ConfigureAwait(false);
        if (update is { } u)
        {
            await alerts.EventUpdatedAsync(evt, u, ct).ConfigureAwait(false);
        }

        return TypedResults.Ok(await BuildDetailAsync(evt, http, user, db, factory, protector, text, ct).ConfigureAwait(false));
    }

    /// <summary>Offers the case to a colleague with a note. Ownership moves only when they accept (SB-43).</summary>
    internal static async Task<Ok<CrisisEventDetail>> HandoverAsync(
        Guid id,
        HandoverRequest request,
        HttpContext http,
        ICurrentUser user,
        TamaneenDbContext db,
        CrisisAlertFactory factory,
        IPatientDataProtector protector,
        ICrisisAlertPublisher alerts,
        IAuditLog audit,
        IStringLocalizer<CrisisResource> text,
        IClock clock,
        CancellationToken ct)
    {
        var note = request.Note?.Trim();
        if (request.ToUserId is not { } to || note is { Length: > 2000 })
        {
            throw new DomainException(ErrorCodes.Validation, "A colleague is required and the note is at most 2000 characters.");
        }

        if (to == user.UserId || !(await RespondersQueryAsync(db, user.UserId!.Value, SupportedLanguages.Default, ct).ConfigureAwait(false)).Any(r => r.UserId == to))
        {
            throw new DomainException(ErrorCodes.Validation, "The colleague cannot take crisis events.").With("errors", new Dictionary<string, string[]> { ["toUserId"] = ["invalid"] });
        }

        var evt = await FindAsync(db, id, ct).ConfigureAwait(false);
        evt.EnsureCanAct(user.UserId!.Value, user.Role);
        evt.RequestHandover(user.UserId!.Value, to, clock);
        var cipher = string.IsNullOrEmpty(note) ? null : await protector.EncryptAsync(evt.PatientId, ActionTextField, note, ct).ConfigureAwait(false);
        db.CrisisActions.Add(CrisisAction.Record(evt, "handover_requested", user.UserId!.Value, user.Role!, cipher, null, to, clock));
        audit.Append("crisis.handover_requested", "CrisisEvent", evt.Id.ToString(), evt.PseudonymId);
        await SaveAsync(db, evt.Id, ct).ConfigureAwait(false);
        await alerts.EventUpdatedAsync(evt, CrisisUpdateKind.Updated, ct).ConfigureAwait(false);
        return TypedResults.Ok(await BuildDetailAsync(evt, http, user, db, factory, protector, text, ct).ConfigureAwait(false));
    }

    internal static Task<Ok<CrisisEventDetail>> AcceptHandoverAsync(Guid id, HttpContext http, ICurrentUser user, TamaneenDbContext db, CrisisAlertFactory factory, IPatientDataProtector protector, ICrisisAlertPublisher alerts, IAuditLog audit, IStringLocalizer<CrisisResource> text, IClock clock, CancellationToken ct) =>
        AnswerHandoverAsync(id, true, http, user, db, factory, protector, alerts, audit, text, clock, ct);

    internal static Task<Ok<CrisisEventDetail>> DeclineHandoverAsync(Guid id, HttpContext http, ICurrentUser user, TamaneenDbContext db, CrisisAlertFactory factory, IPatientDataProtector protector, ICrisisAlertPublisher alerts, IAuditLog audit, IStringLocalizer<CrisisResource> text, IClock clock, CancellationToken ct) =>
        AnswerHandoverAsync(id, false, http, user, db, factory, protector, alerts, audit, text, clock, ct);

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
        else if (!evt.Close(user.UserId!.Value, user.Role!, request.Outcome, request.Reason ?? string.Empty, clock))
        {
            // "Unreachable" from a supervisor waits for the clinical lead; the event stays open (SB-44).
            db.CrisisActions.Add(CrisisAction.Record(evt, "close_requested", user.UserId!.Value, user.Role!, null, request.Outcome, null, clock));
            audit.Append("crisis.close_requested", "CrisisEvent", evt.Id.ToString(), evt.PseudonymId, $"{request.Outcome}: {evt.PendingCloseReason}");
            await SaveAsync(db, evt.Id, cancellationToken).ConfigureAwait(false);
            await alerts.EventUpdatedAsync(evt, CrisisUpdateKind.Updated, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(factory.Create(evt, CrisisUpdateKind.Updated, LocalizationSetup.RequestLanguage(http)));
        }

        await FinishCloseAsync(evt, user, db, audit, clock, cancellationToken).ConfigureAwait(false);
        await alerts.EventUpdatedAsync(evt, CrisisUpdateKind.Closed, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(factory.Create(evt, CrisisUpdateKind.Closed, LocalizationSetup.RequestLanguage(http)));
    }

    /// <summary>The clinical lead signs a supervisor's "unreachable" close; it closes with a 72-hour review entry.</summary>
    internal static async Task<Ok<CrisisAlertMessage>> CoSignCloseAsync(Guid id, HttpContext http, ICurrentUser user, TamaneenDbContext db, IAuditLog audit, ICrisisAlertPublisher alerts, CrisisAlertFactory factory, IClock clock, CancellationToken ct)
    {
        var evt = await FindAsync(db, id, ct).ConfigureAwait(false);
        evt.CoSignClose(user.UserId!.Value, user.Role!, clock);
        db.CrisisActions.Add(CrisisAction.Record(evt, "close_cosigned", user.UserId!.Value, user.Role!, null, evt.CloseOutcome, null, clock));
        audit.Append("crisis.close_cosigned", "CrisisEvent", evt.Id.ToString(), evt.PseudonymId, evt.CloseOutcome);
        await FinishCloseAsync(evt, user, db, audit, clock, ct).ConfigureAwait(false);
        await alerts.EventUpdatedAsync(evt, CrisisUpdateKind.Closed, ct).ConfigureAwait(false);
        return TypedResults.Ok(factory.Create(evt, CrisisUpdateKind.Closed, LocalizationSetup.RequestLanguage(http)));
    }

    /// <summary>Colleagues who can take a handover, without the signed-in user.</summary>
    internal static async Task<Ok<List<Responder>>> RespondersAsync(HttpContext http, ICurrentUser user, TamaneenDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await RespondersQueryAsync(db, user.UserId!.Value, LocalizationSetup.RequestLanguage(http), ct).ConfigureAwait(false));

    private static async Task<List<Responder>> RespondersQueryAsync(TamaneenDbContext db, Guid self, string language, CancellationToken ct)
    {
        var rows = await (from s in db.StaffProfiles.AsNoTracking()
                          join a in db.UserAccounts.AsNoTracking() on s.UserId equals a.Id
                          where s.UserId != self && a.Status == Identity.AccountStatus.Active
                              && (a.Role == AppRoles.Supervisor || a.Role == AppRoles.ClinicalLead || (a.Role == AppRoles.Therapist && s.OnCallTier != null))
                          select new { Profile = s, a.Role })
            .ToListAsync(ct).ConfigureAwait(false);
        return rows
            .Select(r => new Responder(r.Profile.UserId, r.Profile.DisplayName(language), RoleCode(r.Role), r.Profile.OnCallTier is not null))
            .OrderByDescending(r => r.OnCall).ThenBy(r => r.DisplayName, StringComparer.Ordinal)
            .ToList();
    }

    private static string RoleCode(string role) => role switch
    {
        AppRoles.Supervisor => "supervisor",
        AppRoles.ClinicalLead => "lead",
        _ => "therapist",
    };

    private static async Task<Ok<CrisisEventDetail>> AnswerHandoverAsync(Guid id, bool accept, HttpContext http, ICurrentUser user, TamaneenDbContext db, CrisisAlertFactory factory, IPatientDataProtector protector, ICrisisAlertPublisher alerts, IAuditLog audit, IStringLocalizer<CrisisResource> text, IClock clock, CancellationToken ct)
    {
        var evt = await FindAsync(db, id, ct).ConfigureAwait(false);
        if (accept)
        {
            evt.AcceptHandover(user.UserId!.Value);
        }
        else
        {
            evt.CancelHandover(user.UserId!.Value, user.Role);
        }

        var kind = accept ? "handover_accepted" : "handover_declined";
        db.CrisisActions.Add(CrisisAction.Record(evt, kind, user.UserId!.Value, user.Role!, null, clock));
        audit.Append($"crisis.{kind}", "CrisisEvent", evt.Id.ToString(), evt.PseudonymId);
        await SaveAsync(db, evt.Id, ct).ConfigureAwait(false);
        await alerts.EventUpdatedAsync(evt, CrisisUpdateKind.Updated, ct).ConfigureAwait(false);
        return TypedResults.Ok(await BuildDetailAsync(evt, http, user, db, factory, protector, text, ct).ConfigureAwait(false));
    }

    private static async Task FinishCloseAsync(CrisisEvent evt, ICurrentUser user, TamaneenDbContext db, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        // Closing is the only way a locked chat reopens.
        var locks = await db.ChatLocks.Where(l => l.CrisisEventId == evt.Id && l.ReleasedAtUtc == null).ToListAsync(ct).ConfigureAwait(false);
        foreach (var chatLock in locks)
        {
            chatLock.Release(user.UserId!.Value, clock);
        }

        audit.Append("crisis.closed", "CrisisEvent", evt.Id.ToString(), evt.PseudonymId, evt.CloseOutcome is null ? evt.CloseReason : $"{evt.CloseOutcome}: {evt.CloseReason}");
        await SaveAsync(db, evt.Id, ct).ConfigureAwait(false);
    }

    /// <summary>One owner per event: a concurrent change (RowVersion) loses, as for acknowledging.</summary>
    private static async Task SaveAsync(TamaneenDbContext db, Guid id, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException(ErrorCodes.CrisisAlreadyAcknowledged, $"Crisis event {id} changed concurrently.");
        }
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

    private static async Task<CrisisEventDetail> BuildDetailAsync(CrisisEvent evt, HttpContext http, ICurrentUser user, TamaneenDbContext db, CrisisAlertFactory factory, IPatientDataProtector protector, IStringLocalizer<CrisisResource> text, CancellationToken ct)
    {
        var language = LocalizationSetup.RequestLanguage(http);
        var scripts = http.RequestServices.GetRequiredService<CrisisScripts>();
        var contacts = http.RequestServices.GetRequiredService<IOptions<CrisisContactOptions>>().Value;
        var clock = http.RequestServices.GetRequiredService<IClock>();
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
        var actorIds = actions.Select(a => (Guid?)a.ActorUserId).Concat(actions.Select(a => a.TargetUserId)).Concat(audits.Select(a => a.ActorUserId))
            .Append(evt.AcknowledgedByUserId).Append(evt.ClosedByUserId).Append(evt.HandoverToUserId).Append(evt.HandoverFromUserId).Append(evt.PendingCloseByUserId);
        var names = await NamesAsync(db, actorIds, language, ct).ConfigureAwait(false);
        string? Name(Guid? userId) => userId is { } u ? names.GetValueOrDefault(u) : null;
        var now = clock.UtcNow;

        var timeline = LocalizationSetup.WithLanguage(language, () =>
        {
            string ActionText(CrisisAction a) => a.Kind switch
            {
                "call_outcome" => text["timeline.call_outcome", text[$"call.{a.Detail}"].Value].Value,
                "handover_requested" => text["timeline.handover_requested", Name(a.TargetUserId) ?? string.Empty].Value,
                _ => text[$"timeline.{a.Kind}"].Value,
            };

            var entries = new List<TimelineEntry> { new(evt.OpenedAtUtc, "opened", text["timeline.opened", factory.SourceLabel(evt.Source)].Value, null) };
            entries.AddRange(pages.Where(p => p.SentAtUtc is not null).Select(p => new TimelineEntry(p.SentAtUtc!.Value, "paged", text["timeline.paged", text[$"tier.{Math.Min((int)p.Tier, 2)}"].Value].Value, null)));
            if (evt.AcknowledgedAtUtc is { } ackAt)
            {
                entries.Add(new TimelineEntry(ackAt, "acknowledged", text["timeline.acknowledged"].Value, Name(evt.AcknowledgedByUserId)));
            }

            entries.AddRange(actions.Select(a => new TimelineEntry(a.OccurredAtUtc, a.Kind, ActionText(a), Name(a.ActorUserId))));
            entries.AddRange(audits.Select(a => new TimelineEntry(a.OccurredAtUtc, a.Action == "crisis.excerpt_viewed" ? "excerpt_viewed" : "contact_revealed", text[$"timeline.{(a.Action == "crisis.excerpt_viewed" ? "excerpt_viewed" : "contact_revealed")}"].Value, Name(a.ActorUserId))));
            if (evt.ClosedAtUtc is { } closedAt)
            {
                entries.Add(new TimelineEntry(closedAt, "closed", text["timeline.closed"].Value, Name(evt.ClosedByUserId)));
            }

            // What happens next, in the future: the next escalation while nobody took the case, and the review after an unreachable close.
            if (evt.Status == CrisisEventStatus.Open)
            {
                entries.Add(new TimelineEntry(evt.AckDeadlineUtc, "next_escalation", text["timeline.next_escalation", text[$"tier.{Math.Min(evt.EscalationTier + 1, 2)}"].Value].Value, null));
            }

            if (evt.ReviewDueAtUtc is { } reviewAt)
            {
                entries.Add(new TimelineEntry(reviewAt, "review_due", text["timeline.review_due"].Value, null));
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

        // EC-33: three attempts in 15 minutes without reaching a Critical patient: local emergency if the location
        // is known, otherwise close as unreachable with the clinical lead's co-sign.
        var since = now - CallAttemptWindow;
        var recentCalls = actions.Count(a => a.Kind == "call_attempt" && a.OccurredAtUtc >= since);
        var reached = evt.Status == CrisisEventStatus.InContact || actions.Any(a => a.Kind == "call_outcome" && a.Detail == "answered" && a.OccurredAtUtc >= since);
        var locationKnown = profile?.ShareLocation ?? false;
        var advice = evt.Level >= RiskLevel.Critical && evt.Status != CrisisEventStatus.Closed && recentCalls >= CallAttemptsBeforeAdvice && !reached
            ? (locationKnown ? "local_emergency" : "unreachable")
            : null;

        var userId = user.UserId!.Value;
        var pendingHandover = evt.HandoverToUserId is { } to
            ? new PendingHandover(to, Name(to), Name(evt.HandoverFromUserId), evt.HandoverRequestedAtUtc!.Value, to == userId)
            : null;
        var pendingClose = evt.PendingCloseOutcome is { } outcome
            ? new PendingClose(outcome, evt.PendingCloseReason ?? string.Empty, Name(evt.PendingCloseByUserId), evt.PendingCloseAtUtc!.Value)
            : null;
        var script = scripts.Render(locationKnown ? CrisisScriptKind.ResponderEmergency : CrisisScriptKind.ResponderNoLocation, language, contacts.EmergencyNumber, contacts.SupportLine);

        var a = factory.Create(evt, null, language, Name(evt.AcknowledgedByUserId));
        return new CrisisEventDetail(
            a.EventId, a.Kind, a.PseudonymId, a.Level, a.LevelLabel, a.Title, a.Body, a.PatientLanguage, a.PatientLanguageName,
            a.EscalationTier, a.EscalationTierLabel, a.Status, a.OpenedAtUtc, a.AckDeadlineUtc, a.Categories, a.Dir, a.Number, a.Source, a.SourceLabel, a.AcknowledgedBy,
            evt.TriggerRuleIds.Split(',', StringSplitOptions.RemoveEmptyEntries),
            evt.RulePackVersion,
            evt.ContactDeadlineUtc,
            phone is not null,
            safety?.TrustedName is not null || safety?.TrustedPhone is not null,
            locationKnown,
            masked,
            timeline,
            user.Role is AppRoles.Supervisor or AppRoles.ClinicalLead && evt.Status != CrisisEventStatus.Closed && pendingClose is null,
            evt.Status != CrisisEventStatus.Closed && evt.CanAct(userId, user.Role),
            evt.IsOwned && evt.AcknowledgedByUserId == userId,
            pendingHandover,
            pendingClose,
            user.Role == AppRoles.ClinicalLead && pendingClose is not null,
            contacts.EmergencyNumber,
            script,
            recentCalls,
            advice,
            evt.ReviewDueAtUtc);
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
