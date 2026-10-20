using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tamaneen.Crisis.Assessment;
using Tamaneen.Crisis.Rules;
using Tamaneen.SharedKernel;

namespace Tamaneen.Crisis.Gate;

/// <summary>
/// The first and only check every patient message passes before anything else happens to it.
/// <list type="number">
/// <item>If the patient's chat is locked, the message is blocked and the crisis screen shown again.</item>
/// <item>Otherwise the words (and any questionnaire answers) are assessed against the rule packs.</item>
/// <item>High or Critical: one transaction opens or raises the crisis event, locks the chat, queues pages for the
///       on-call clinician and writes audit; then the console is alerted live. The AI is never called.</item>
/// <item>Anything unexpected fails closed: the patient sees crisis resources and a system crisis event pages a human.</item>
/// </list>
/// </summary>
public sealed partial class CrisisGate
{
    public const string SystemFailureRuleId = "SYS-GATE-FAILURE";

    /// <summary>The patient pressed "ask a professional to call me now": always a Critical event.</summary>
    public const string HelpNowRuleId = "PATIENT-HELP-NOW";

    public const string HelpNowSource = "help_now";

    private readonly IRiskEvaluator _evaluator;
    private readonly ICrisisCaseStore _store;
    private readonly ICrisisAlertPublisher _alerts;
    private readonly CrisisScripts _scripts;
    private readonly CrisisSlaOptions _sla;
    private readonly CrisisContactOptions _contacts;
    private readonly IClock _clock;
    private readonly ILogger<CrisisGate> _log;

    public CrisisGate(
        IRiskEvaluator evaluator,
        ICrisisCaseStore store,
        ICrisisAlertPublisher alerts,
        CrisisScripts scripts,
        IOptions<CrisisSlaOptions> sla,
        IOptions<CrisisContactOptions> contacts,
        IClock clock,
        ILogger<CrisisGate> log)
    {
        _evaluator = evaluator;
        _store = store;
        _alerts = alerts;
        _scripts = scripts;
        _sla = sla.Value;
        _contacts = contacts.Value;
        _clock = clock;
        _log = log;
    }

    public async Task<GateResult> EvaluateAsync(GateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var language = SupportedLanguages.Normalize(request.Language);
        try
        {
            var assessment = _evaluator.Assess(request.Text, request.Answers);
            if (request.Source == HelpNowSource)
            {
                assessment = new RiskAssessment(RiskLevel.Critical, [.. assessment.Hits, new RuleHit(HelpNowRuleId, RiskLevel.Critical, "patient_request", RuleSource.System)], assessment.RulesVersion);
            }

            var activeLock = await _store.FindActiveLockAsync(request.PatientId, cancellationToken).ConfigureAwait(false);
            if (!assessment.IsCrisis && activeLock is null)
            {
                return GateResult.Pass(assessment.Level, assessment.RuleIds);
            }

            var pseudonym = await _store.FindPseudonymAsync(request.PatientId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Patient {request.PatientId} has no account.");
            var open = await _store.FindOpenEventAsync(request.PatientId, cancellationToken).ConfigureAwait(false);

            if (!assessment.IsCrisis)
            {
                // Locked and nothing new: keep showing the crisis screen, do not page again.
                var lockedEvent = open ?? await _store.FindEventAsync(activeLock!.CrisisEventId, cancellationToken).ConfigureAwait(false);
                var level = lockedEvent?.Level ?? RiskLevel.High;
                return GateResult.Blocked(lockedEvent?.Id, level, Script(level, language), locked: true, assessment.RuleIds);
            }

            CrisisEvent crisisEvent;
            var opened = false;
            var raised = false;
            if (open is null)
            {
                crisisEvent = CrisisEvent.Open(request.PatientId, pseudonym, assessment.Level, language, assessment.RulesVersion, _sla.AcknowledgeWithin(assessment.Level), assessment.RuleIds, request.Source, _clock);
                _store.Add(crisisEvent);
                _store.Add(PageRequest.For(crisisEvent, _clock));
                _store.Audit("crisis.opened", crisisEvent);
                opened = true;
            }
            else
            {
                crisisEvent = open;
                var before = crisisEvent.Level;
                crisisEvent.Escalate(assessment.Level);
                crisisEvent.AddTriggers(assessment.RuleIds);
                if (crisisEvent.Level > before)
                {
                    // Worse news on an open case pages again at the current tier.
                    _store.Add(PageRequest.For(crisisEvent, _clock));
                    _store.Audit("crisis.raised", crisisEvent);
                    raised = true;
                }
            }

            if (activeLock is null)
            {
                _store.Add(ChatLock.For(crisisEvent, _clock));
            }

            if (!string.IsNullOrWhiteSpace(request.Text))
            {
                // Kept encrypted for the responder, who sees it only after a second verification.
                await _store.AttachExcerptAsync(crisisEvent, request.Text, cancellationToken).ConfigureAwait(false);
            }

            await _store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await PublishAsync(crisisEvent, opened, raised, cancellationToken).ConfigureAwait(false);
            return GateResult.Blocked(crisisEvent.Id, crisisEvent.Level, Script(crisisEvent.Level, language), locked: true, assessment.RuleIds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogGateFailure(_log, request.PatientId, ex);
            return await FailClosedAsync(request, language, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<GateResult> FailClosedAsync(GateRequest request, string language, CancellationToken cancellationToken)
    {
        var script = _scripts.Render(CrisisScriptKind.SystemFailure, language, _contacts.EmergencyNumber, _contacts.SupportLine);
        try
        {
            var pseudonym = await _store.FindPseudonymAsync(request.PatientId, cancellationToken).ConfigureAwait(false);
            if (pseudonym is null)
            {
                return GateResult.Blocked(null, RiskLevel.High, script, locked: false, new[] { SystemFailureRuleId });
            }

            var crisisEvent = CrisisEvent.Open(request.PatientId, pseudonym, RiskLevel.High, language, _evaluator.Version, _sla.AcknowledgeWithin(RiskLevel.High), new[] { SystemFailureRuleId }, "system", _clock);
            _store.Add(crisisEvent);
            _store.Add(ChatLock.For(crisisEvent, _clock));
            _store.Add(PageRequest.For(crisisEvent, _clock));
            _store.Audit("crisis.system_failure", crisisEvent);
            await _store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await PublishAsync(crisisEvent, opened: true, raised: false, cancellationToken).ConfigureAwait(false);
            return GateResult.Blocked(crisisEvent.Id, RiskLevel.High, script, locked: true, new[] { SystemFailureRuleId });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The database is unreachable too. The patient still gets crisis resources; the health check pages engineers.
            LogFailClosedFailure(_log, request.PatientId, ex);
            return GateResult.Blocked(null, RiskLevel.High, script, locked: false, new[] { SystemFailureRuleId });
        }
    }

    private async Task PublishAsync(CrisisEvent crisisEvent, bool opened, bool raised, CancellationToken cancellationToken)
    {
        try
        {
            if (opened)
            {
                await _alerts.EventOpenedAsync(crisisEvent, cancellationToken).ConfigureAwait(false);
            }
            else if (raised)
            {
                await _alerts.EventUpdatedAsync(crisisEvent, CrisisUpdateKind.Raised, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPublishFailure(_log, crisisEvent.Id, ex);
        }
    }

    private string Script(RiskLevel level, string language) =>
        _scripts.Render(CrisisScripts.KindFor(level), language, _contacts.EmergencyNumber, _contacts.SupportLine);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Crisis gate failed for patient {PatientId}; failing closed")]
    private static partial void LogGateFailure(ILogger logger, Guid patientId, Exception exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Could not record the fail-closed crisis event for patient {PatientId}")]
    private static partial void LogFailClosedFailure(ILogger logger, Guid patientId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Live alert for crisis event {EventId} failed; paging will still reach the on-call clinician")]
    private static partial void LogPublishFailure(ILogger logger, Guid eventId, Exception exception);
}

/// <summary>What the API sends for each patient message, questionnaire answer or journal entry.</summary>
public sealed record GateRequest(Guid PatientId, string Language, string? Text, QuestionnaireAnswers? Answers, string Source);

public enum GateDecision
{
    Pass = 0,
    Blocked = 1,
}

public sealed record GateResult(GateDecision Decision, RiskLevel Level, Guid? CrisisEventId, string? Script, bool ChatLocked, IReadOnlyList<string> RuleIds)
{
    public static GateResult Pass(RiskLevel level, IReadOnlyList<string> ruleIds) => new(GateDecision.Pass, level, null, null, false, ruleIds);

    public static GateResult Blocked(Guid? eventId, RiskLevel level, string script, bool locked, IReadOnlyList<string> ruleIds) =>
        new(GateDecision.Blocked, level, eventId, script, locked, ruleIds);
}
