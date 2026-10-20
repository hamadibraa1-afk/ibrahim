using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tamaneen.Crisis;
using Tamaneen.Crisis.Assessment;
using Tamaneen.Crisis.Gate;
using Tamaneen.Crisis.Rules;
using Tamaneen.SharedKernel;

namespace Tamaneen.UnitTests.Crisis;

public class CrisisGateTests
{
    private static readonly CrisisSlaOptions Sla = new()
    {
        CriticalAcknowledgeSeconds = 120,
        CriticalContactSeconds = 300,
        HighAcknowledgeSeconds = 900,
        HighContactSeconds = 3600,
        MaxEscalationTier = 2,
    };

    private readonly TestClock _clock = new(new DateTimeOffset(2026, 10, 20, 22, 0, 0, TimeSpan.Zero));
    private readonly FakeStore _store = new();
    private readonly FakeAlerts _alerts = new();
    private readonly Guid _patient = Guid.NewGuid();

    public CrisisGateTests() => _store.Pseudonyms[_patient] = "P-AB12-CD34";

    private CrisisGate Gate(IRiskEvaluator? evaluator = null) => new(
        evaluator ?? CrisisRuleCatalog.CreateEvaluator(),
        _store,
        _alerts,
        CrisisRuleCatalog.LoadScripts(),
        Options.Create(Sla),
        Options.Create(new CrisisContactOptions { EmergencyNumber = "911", SupportLine = "920033360" }),
        _clock,
        NullLogger<CrisisGate>.Instance);

    private GateRequest Message(string text, string language = "ar") => new(_patient, language, text, null, "chat");

    [Fact]
    public async Task Ordinary_messages_pass_without_touching_the_database()
    {
        var result = await Gate().EvaluateAsync(Message("الجلسة ساعدتني كثير"), default);
        result.Decision.Should().Be(GateDecision.Pass);
        result.Script.Should().BeNull();
        _store.Saves.Should().Be(0);
        _alerts.Opened.Should().BeEmpty();
    }

    [Fact]
    public async Task Critical_text_opens_an_event_locks_the_chat_pages_and_alerts_in_one_save()
    {
        var result = await Gate().EvaluateAsync(Message("سأنتحر الليلة"), default);

        result.Decision.Should().Be(GateDecision.Blocked);
        result.Level.Should().Be(RiskLevel.Critical);
        result.ChatLocked.Should().BeTrue();
        result.Script.Should().StartWith("أنت لست وحدك").And.Contain("911").And.Contain("920033360");

        var evt = _store.Events.Should().ContainSingle().Subject;
        evt.Level.Should().Be(RiskLevel.Critical);
        evt.AckDeadlineUtc.Should().Be(_clock.UtcNow.AddMinutes(2));
        evt.Language.Should().Be("ar");
        evt.TriggerRuleIds.Should().Contain("AR-C-INTENT");
        evt.Source.Should().Be("chat");
        _store.Locks.Should().ContainSingle(l => l.CrisisEventId == evt.Id && l.IsActive);
        _store.Pages.Should().ContainSingle(p => p.CrisisEventId == evt.Id && p.Tier == 0);
        _store.AuditActions.Should().Equal("crisis.opened");
        _store.Saves.Should().Be(1);
        _alerts.Opened.Should().ContainSingle().Which.Should().BeSameAs(evt);
    }

    [Fact]
    public async Task Help_now_without_any_text_opens_a_critical_event()
    {
        var result = await Gate().EvaluateAsync(new GateRequest(_patient, "en", null, null, CrisisGate.HelpNowSource), default);

        result.Decision.Should().Be(GateDecision.Blocked);
        result.Level.Should().Be(RiskLevel.Critical);
        var evt = _store.Events.Should().ContainSingle().Subject;
        evt.TriggerRuleIds.Should().Contain(CrisisGate.HelpNowRuleId);
        evt.Source.Should().Be("help_now");
        _store.Excerpts.Should().BeEmpty();
    }

    [Fact]
    public async Task The_triggering_words_are_kept_for_the_responder()
    {
        await Gate().EvaluateAsync(Message("سأنتحر الليلة"), default);

        _store.Excerpts.Should().Equal("سأنتحر الليلة");
        _store.Events.Single().ExcerptCipher.Should().NotBeNull();
    }

    [Fact]
    public async Task High_text_gets_the_high_script_in_english_and_a_15_minute_window()
    {
        var result = await Gate().EvaluateAsync(Message("I want to die", "en-GB"), default);
        result.Level.Should().Be(RiskLevel.High);
        result.Script.Should().StartWith("Thank you for telling us");
        _store.Events.Single().AckDeadlineUtc.Should().Be(_clock.UtcNow.AddMinutes(15));
        _store.Events.Single().Language.Should().Be("en");
    }

    [Fact]
    public async Task While_locked_every_message_is_blocked_and_saying_it_was_a_joke_changes_nothing()
    {
        var gate = Gate();
        await gate.EvaluateAsync(Message("سأنتحر الليلة"), default);
        var result = await gate.EvaluateAsync(Message("كنت أمزح، أنا بخير"), default);

        result.Decision.Should().Be(GateDecision.Blocked);
        result.ChatLocked.Should().BeTrue();
        result.Level.Should().Be(RiskLevel.Critical);
        _store.Events.Should().HaveCount(1);
        _store.Pages.Should().HaveCount(1, "a benign follow-up must not page again");
        _store.Saves.Should().Be(1);
    }

    [Fact]
    public async Task Worse_news_on_an_open_event_raises_the_level_and_pages_again()
    {
        var gate = Gate();
        await gate.EvaluateAsync(Message("ابي اموت"), default);
        _store.Events.Single().Level.Should().Be(RiskLevel.High);

        var result = await gate.EvaluateAsync(Message("جمعت حبوب كثير"), default);
        result.Level.Should().Be(RiskLevel.Critical);
        _store.Events.Should().HaveCount(1);
        _store.Events.Single().Level.Should().Be(RiskLevel.Critical);
        _store.Events.Single().TriggerRuleIds.Should().Contain("AR-H-IDEATION").And.Contain("AR-C-MEANS");
        _store.Pages.Should().HaveCount(2);
        _store.Locks.Should().HaveCount(1);
        _alerts.Updated.Should().ContainSingle(u => u.Kind == CrisisUpdateKind.Raised);
    }

    [Fact]
    public async Task Questionnaire_answers_alone_can_open_an_event()
    {
        var answers = new QuestionnaireAnswers("C-SSRS-SCREEN", new Dictionary<int, int> { [4] = 1 });
        var result = await Gate().EvaluateAsync(new GateRequest(_patient, "ar", null, answers, "intake"), default);
        result.Level.Should().Be(RiskLevel.Critical);
        _store.Events.Single().TriggerRuleIds.Should().Be("Q-CSSRS-4");
    }

    [Fact]
    public async Task If_assessment_throws_the_gate_fails_closed_with_a_system_event()
    {
        var result = await Gate(new ThrowingEvaluator()).EvaluateAsync(Message("hello", "en"), default);
        result.Decision.Should().Be(GateDecision.Blocked);
        result.RuleIds.Should().Equal(CrisisGate.SystemFailureRuleId);
        result.Script.Should().StartWith("We're having a technical problem").And.Contain("911");
        result.ChatLocked.Should().BeTrue();
        _store.Events.Single().TriggerRuleIds.Should().Be(CrisisGate.SystemFailureRuleId);
        _store.Pages.Should().ContainSingle();
        _store.AuditActions.Should().Equal("crisis.system_failure");
    }

    [Fact]
    public async Task If_the_database_is_down_the_patient_still_gets_crisis_resources()
    {
        _store.FailEverything = true;
        var result = await Gate().EvaluateAsync(Message("سأنتحر"), default);
        result.Decision.Should().Be(GateDecision.Blocked);
        result.Script.Should().StartWith("نواجه مشكلة تقنية").And.Contain("911");
        result.CrisisEventId.Should().BeNull();
    }

    [Fact]
    public async Task A_failing_live_alert_does_not_lose_the_event_or_the_page()
    {
        _alerts.Fail = true;
        var result = await Gate().EvaluateAsync(Message("سأنتحر"), default);
        result.Decision.Should().Be(GateDecision.Blocked);
        result.CrisisEventId.Should().NotBeNull();
        _store.Pages.Should().ContainSingle();
    }

    [Fact]
    public async Task Cancellation_is_not_mistaken_for_a_failure()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _store.CancelOnRead = true;
        var act = () => Gate().EvaluateAsync(Message("سأنتحر"), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class ThrowingEvaluator : IRiskEvaluator
    {
        public string Version => "broken";

        public RiskAssessment Assess(string? text, QuestionnaireAnswers? answers = null) => throw new InvalidOperationException("rule engine down");
    }

    private sealed class FakeAlerts : ICrisisAlertPublisher
    {
        public bool Fail { get; set; }

        public List<CrisisEvent> Opened { get; } = new();

        public List<(CrisisEvent Event, CrisisUpdateKind Kind)> Updated { get; } = new();

        public Task EventOpenedAsync(CrisisEvent crisisEvent, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new HttpRequestException("hub down");
            }

            Opened.Add(crisisEvent);
            return Task.CompletedTask;
        }

        public Task EventUpdatedAsync(CrisisEvent crisisEvent, CrisisUpdateKind kind, CancellationToken cancellationToken)
        {
            Updated.Add((crisisEvent, kind));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStore : ICrisisCaseStore
    {
        public Dictionary<Guid, string> Pseudonyms { get; } = new();

        public List<CrisisEvent> Events { get; } = new();

        public List<ChatLock> Locks { get; } = new();

        public List<PageRequest> Pages { get; } = new();

        public List<string> AuditActions { get; } = new();

        public int Saves { get; private set; }

        public List<string> Excerpts { get; } = new();

        public Task AttachExcerptAsync(CrisisEvent crisisEvent, string text, CancellationToken cancellationToken)
        {
            Excerpts.Add(text);
            crisisEvent.SetExcerpt(System.Text.Encoding.UTF8.GetBytes(text));
            return Task.CompletedTask;
        }

        public bool FailEverything { get; set; }

        public bool CancelOnRead { get; set; }

        private void Check(CancellationToken ct)
        {
            if (CancelOnRead)
            {
                ct.ThrowIfCancellationRequested();
            }

            if (FailEverything)
            {
                throw new TimeoutException("database unreachable");
            }
        }

        public Task<string?> FindPseudonymAsync(Guid patientId, CancellationToken cancellationToken)
        {
            Check(cancellationToken);
            return Task.FromResult(Pseudonyms.GetValueOrDefault(patientId));
        }

        public Task<CrisisEvent?> FindOpenEventAsync(Guid patientId, CancellationToken cancellationToken)
        {
            Check(cancellationToken);
            return Task.FromResult(Events.FirstOrDefault(e => e.PatientId == patientId && e.Status != CrisisEventStatus.Closed));
        }

        public Task<ChatLock?> FindActiveLockAsync(Guid patientId, CancellationToken cancellationToken)
        {
            Check(cancellationToken);
            return Task.FromResult(Locks.FirstOrDefault(l => l.PatientId == patientId && l.IsActive));
        }

        public Task<CrisisEvent?> FindEventAsync(Guid eventId, CancellationToken cancellationToken)
        {
            Check(cancellationToken);
            return Task.FromResult(Events.FirstOrDefault(e => e.Id == eventId));
        }

        public void Add(CrisisEvent crisisEvent) => Events.Add(crisisEvent);

        public void Add(ChatLock chatLock) => Locks.Add(chatLock);

        public void Add(PageRequest pageRequest) => Pages.Add(pageRequest);

        public void Audit(string action, CrisisEvent crisisEvent, string? reason = null) => AuditActions.Add(action);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            Check(cancellationToken);
            Saves++;
            return Task.CompletedTask;
        }
    }
}

public class CrisisEscalationTests
{
    private readonly TestClock _clock = new(DateTimeOffset.UnixEpoch);

    [Fact]
    public void Each_missed_deadline_moves_to_the_next_tier_and_the_lead_keeps_being_paged()
    {
        var evt = CrisisEvent.Open(Guid.NewGuid(), "P-AB12-CD34", RiskLevel.Critical, "ar", "v1", TimeSpan.FromMinutes(2), new[] { "R1" }, "chat", _clock);
        _clock.Advance(TimeSpan.FromMinutes(3));
        evt.EscalateToNextTier(TimeSpan.FromMinutes(2), 2, _clock).Should().Be(1);
        evt.AckDeadlineUtc.Should().Be(_clock.UtcNow.AddMinutes(2));
        evt.EscalateToNextTier(TimeSpan.FromMinutes(2), 2, _clock).Should().Be(2);
        evt.EscalateToNextTier(TimeSpan.FromMinutes(2), 2, _clock).Should().Be(2);
    }

    [Fact]
    public void Acknowledged_events_do_not_escalate_and_get_a_contact_deadline()
    {
        var evt = CrisisEvent.Open(Guid.NewGuid(), "P-AB12-CD34", RiskLevel.Critical, "ar", "v1", TimeSpan.FromMinutes(2), new[] { "R1" }, "chat", _clock);
        evt.Acknowledge(Guid.NewGuid(), TimeSpan.FromMinutes(5), _clock);
        evt.ContactDeadlineUtc.Should().Be(_clock.UtcNow.AddMinutes(5));
        var act = () => evt.EscalateToNextTier(TimeSpan.FromMinutes(2), 2, _clock);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Triggers_are_merged_without_duplicates_and_never_contain_text()
    {
        var evt = CrisisEvent.Open(Guid.NewGuid(), "P-AB12-CD34", RiskLevel.High, "en", "v1", TimeSpan.FromMinutes(15), new[] { "A", "B" }, "intake", _clock);
        evt.AddTriggers(new[] { "B", "C" });
        evt.TriggerRuleIds.Should().Be("A,B,C");
    }

    [Fact]
    public void Failed_pages_retry_quickly_and_never_wait_more_than_a_minute()
    {
        var evt = CrisisEvent.Open(Guid.NewGuid(), "P-AB12-CD34", RiskLevel.Critical, "ar", "v1", TimeSpan.FromMinutes(2), _clock);
        var page = PageRequest.For(evt, _clock);
        page.MarkFailed("timeout", _clock);
        page.NextAttemptAtUtc.Should().Be(_clock.UtcNow.AddSeconds(5));
        for (var i = 0; i < 10; i++)
        {
            page.MarkFailed("timeout", _clock);
        }

        page.NextAttemptAtUtc.Should().Be(_clock.UtcNow.AddSeconds(60));
        page.MarkSent(_clock);
        page.SentAtUtc.Should().Be(_clock.UtcNow);
    }

    [Fact]
    public void A_chat_lock_is_released_once_by_a_clinician()
    {
        var evt = CrisisEvent.Open(Guid.NewGuid(), "P-AB12-CD34", RiskLevel.Critical, "ar", "v1", TimeSpan.FromMinutes(2), _clock);
        var chatLock = ChatLock.For(evt, _clock);
        chatLock.IsActive.Should().BeTrue();
        var clinician = Guid.NewGuid();
        chatLock.Release(clinician, _clock);
        chatLock.Release(Guid.NewGuid(), _clock);
        chatLock.IsActive.Should().BeFalse();
        chatLock.ReleasedByUserId.Should().Be(clinician);
    }
}
