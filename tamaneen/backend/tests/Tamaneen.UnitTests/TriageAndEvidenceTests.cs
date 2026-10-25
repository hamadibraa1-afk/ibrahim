using Tamaneen.Api.Clinical;
using Tamaneen.Api.Endpoints;
using Tamaneen.Clinical.Intake;
using Tamaneen.SharedKernel;

namespace Tamaneen.UnitTests;

public class TriageCaseTests
{
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero));
    private readonly Guid _supervisor = Guid.NewGuid();
    private readonly Guid _lead = Guid.NewGuid();

    private TriageCase Case(RiskLevel level) => TriageCase.FromIntake(Guid.NewGuid(), Guid.NewGuid(), level, 10, 8, _clock);

    [Theory]
    [InlineData(RiskLevel.Routine, 24)]
    [InlineData(RiskLevel.Elevated, 4)]
    [InlineData(RiskLevel.High, 1)]
    public void The_review_deadline_follows_the_level(RiskLevel level, int hours)
    {
        Case(level).DueAtUtc.Should().Be(_clock.UtcNow.AddHours(hours));
    }

    [Fact]
    public void Raising_the_risk_brings_the_deadline_forward_and_never_lowers()
    {
        var triage = Case(RiskLevel.Routine);
        triage.RaiseRisk(RiskLevel.Elevated);
        triage.DueAtUtc.Should().Be(_clock.UtcNow.AddHours(4));
        triage.RaiseRisk(RiskLevel.Routine);
        triage.RiskLevel.Should().Be(RiskLevel.Elevated);
        triage.RuleRiskLevel.Should().Be(RiskLevel.Routine);
    }

    [Fact]
    public void A_claim_lasts_fifteen_minutes_and_blocks_other_supervisors()
    {
        var triage = Case(RiskLevel.Routine);
        triage.Claim(_supervisor, _clock);
        triage.ClaimedUntilUtc.Should().Be(_clock.UtcNow.AddMinutes(15));
        var other = () => triage.Claim(_lead, _clock);
        other.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.TriageClaimedByOther);

        _clock.Advance(TimeSpan.FromMinutes(16));
        var decide = () => triage.Approve(_supervisor, _clock);
        decide.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.TriageClaimRequired);
        triage.Claim(_lead, _clock);
        triage.IsClaimedBy(_lead, _clock.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void Decisions_other_than_approve_need_a_reason()
    {
        var triage = Case(RiskLevel.Routine);
        triage.Claim(_supervisor, _clock);
        var act = () => triage.OverrideRisk(_supervisor, false, RiskLevel.Elevated, RiskLevel.None, " ", _clock);
        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.ReasonRequired);
        var close = () => triage.Close(_supervisor, ["primary_care"], null, _clock);
        close.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.ReasonRequired);
    }

    [Fact]
    public void Lowering_from_high_waits_for_the_clinical_lead_and_never_goes_below_the_crisis_floor()
    {
        var triage = Case(RiskLevel.High);
        triage.Claim(_supervisor, _clock);

        triage.OverrideRisk(_supervisor, false, RiskLevel.Routine, RiskLevel.None, "spoke with patient", _clock).Should().Be(RiskChange.CoSignRequested);
        triage.RiskLevel.Should().Be(RiskLevel.High);
        triage.PendingRiskLevel.Should().Be(RiskLevel.Routine);
        var self = () => triage.CoSign(_supervisor, true, RiskLevel.None);
        self.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.TriageCoSignNotAllowed);
        var floor = () => triage.CoSign(_lead, true, RiskLevel.High);
        floor.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.TriageBelowCrisisFloor);

        triage.CoSign(_lead, true, RiskLevel.None);
        triage.RiskLevel.Should().Be(RiskLevel.Routine);
        triage.PendingRiskLevel.Should().BeNull();
        triage.DueAtUtc.Should().Be(triage.SubmittedAtUtc.AddHours(24));
    }

    [Fact]
    public void The_clinical_leads_own_lowering_applies_but_still_respects_the_floor()
    {
        var triage = Case(RiskLevel.Critical);
        triage.Claim(_lead, _clock);
        var below = () => triage.OverrideRisk(_lead, true, RiskLevel.Elevated, RiskLevel.High, "calmer", _clock);
        below.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.TriageBelowCrisisFloor);
        triage.OverrideRisk(_lead, true, RiskLevel.High, RiskLevel.High, "calmer", _clock).Should().Be(RiskChange.Applied);
        triage.RiskLevel.Should().Be(RiskLevel.High);
    }

    [Fact]
    public void The_single_step_review_cannot_lower_a_level()
    {
        var triage = Case(RiskLevel.Elevated);
        var act = () => triage.Review(_supervisor, RiskLevel.Routine, RiskLevel.None, _clock);
        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.TriageLoweringNeedsDecision);
        triage.Review(_supervisor, RiskLevel.Elevated, RiskLevel.None, _clock);
        triage.Status.Should().Be(TriageStatus.Reviewed);
    }

    [Fact]
    public void Approve_is_blocked_while_a_co_sign_is_pending_and_close_records_referrals()
    {
        var triage = Case(RiskLevel.High);
        triage.Claim(_supervisor, _clock);
        triage.OverrideRisk(_supervisor, false, RiskLevel.Elevated, RiskLevel.None, "review", _clock);
        var approve = () => triage.Approve(_supervisor, _clock);
        approve.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.TriageCoSignPending);
        triage.CoSign(_lead, false, RiskLevel.None);
        var unknown = () => triage.Close(_supervisor, ["somewhere"], "x", _clock);
        unknown.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.Validation);
        triage.Close(_supervisor, ["primary_care", "crisis_line"], "better served elsewhere", _clock);
        triage.Status.Should().Be(TriageStatus.Closed);
        triage.Referrals.Should().Be("primary_care,crisis_line");
        triage.ClaimedByUserId.Should().BeNull();
    }
}

public class EvidenceLinkerTests
{
    private static readonly TranscriptTurn[] Transcript =
    [
        new(Guid.NewGuid(), 1, false, "أهلًا بك، كيف حالك؟"),
        new(Guid.NewGuid(), 2, true, "أشعر بضغط كبير في الشغل وما أنام زين"),
        new(Guid.NewGuid(), 3, false, "متى بدأ ذلك؟"),
        new(Guid.NewGuid(), 4, true, "من ثلاثة أشهر بعد ما تغير مديري"),
    ];

    [Fact]
    public void A_trailing_citation_is_split_off_and_resolved_to_patient_messages_only()
    {
        var (text, turns) = EvidenceLinker.SplitCitation("تشعر بضغط كبير منذ ثلاثة أشهر. [2, 3, 4]");
        text.Should().Be("تشعر بضغط كبير منذ ثلاثة أشهر.");
        turns.Should().Equal(2, 3, 4);
        EvidenceLinker.Resolve(turns, Transcript).Should().Equal(Transcript[1].Id, Transcript[3].Id);
    }

    [Fact]
    public void Text_without_a_citation_is_left_alone()
    {
        EvidenceLinker.SplitCitation("Nothing to cite [here].").Should().Be(("Nothing to cite [here].", Array.Empty<int>()));
    }

    [Fact]
    public void Without_a_citation_the_messages_sharing_words_are_the_sources()
    {
        EvidenceLinker.MatchLexically("تشعر بضغط كبير في العمل", Transcript).Should().Equal(Transcript[1].Id);
        EvidenceLinker.MatchLexically("لم تراجع مختصًا من قبل", Transcript).Should().BeEmpty();
    }

    [Fact]
    public void Numbered_transcript_marks_who_spoke()
    {
        EvidenceLinker.NumberedTranscript(Transcript).Split('\n')[1].Should().Be("[2] Patient: أشعر بضغط كبير في الشغل وما أنام زين");
    }

    [Theory]
    [InlineData("PHQ-9", 4, "minimal")]
    [InlineData("PHQ-9", 16, "moderately_severe")]
    [InlineData("PHQ-9", 20, "severe")]
    [InlineData("GAD-7", 12, "moderate")]
    [InlineData("GAD-7", 15, "severe")]
    public void Scores_get_the_standard_severity_bands(string instrument, int total, string severity)
    {
        StaffFileEndpoints.Severity(instrument, total).Should().Be(severity);
    }
}
