using Tamaneen.AiGateway;
using Tamaneen.Clinical;
using Tamaneen.Crisis;
using Tamaneen.SharedKernel;

namespace Tamaneen.UnitTests;

public class CrisisEventTests
{
    private const string Pseudonym = "P-AB12-CD34";
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero));

    private CrisisEvent Critical() =>
        CrisisEvent.Open(Guid.NewGuid(), Pseudonym, RiskLevel.Critical, "en-US", "rules-ar-1.4", TimeSpan.FromMinutes(2), _clock);

    [Fact]
    public void Open_sets_the_acknowledgement_deadline_and_language()
    {
        var evt = Critical();
        evt.Status.Should().Be(CrisisEventStatus.Open);
        evt.AckDeadlineUtc.Should().Be(_clock.UtcNow.AddMinutes(2));
        evt.Language.Should().Be("en");
    }

    [Theory]
    [InlineData(RiskLevel.Routine)]
    [InlineData(RiskLevel.Elevated)]
    public void Only_high_and_critical_open_an_event(RiskLevel level)
    {
        var act = () => CrisisEvent.Open(Guid.NewGuid(), Pseudonym, level, "ar", "v1", TimeSpan.FromMinutes(2), _clock);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Overdue_only_while_open_and_past_the_deadline()
    {
        var evt = Critical();
        evt.IsAcknowledgementOverdue(_clock).Should().BeFalse();
        _clock.Advance(TimeSpan.FromSeconds(121));
        evt.IsAcknowledgementOverdue(_clock).Should().BeTrue();
        evt.Acknowledge(Guid.NewGuid(), _clock);
        evt.IsAcknowledgementOverdue(_clock).Should().BeFalse();
    }

    [Fact]
    public void A_second_acknowledgement_is_a_conflict()
    {
        var evt = Critical();
        evt.Acknowledge(Guid.NewGuid(), _clock);
        var again = () => evt.Acknowledge(Guid.NewGuid(), _clock);
        again.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.CrisisAlreadyAcknowledged);
    }

    [Fact]
    public void Level_can_rise_but_never_fall()
    {
        var evt = CrisisEvent.Open(Guid.NewGuid(), Pseudonym, RiskLevel.High, "ar", "v1", TimeSpan.FromMinutes(15), _clock);
        evt.Escalate(RiskLevel.Critical);
        evt.Level.Should().Be(RiskLevel.Critical);
        evt.Escalate(RiskLevel.Routine);
        evt.Level.Should().Be(RiskLevel.Critical);
    }

    [Theory]
    [InlineData(AppRoles.Therapist)]
    [InlineData(AppRoles.Admin)]
    public void Only_supervisor_or_lead_can_close(string role)
    {
        var evt = Critical();
        var act = () => evt.Close(Guid.NewGuid(), role, "contacted, safe", _clock);
        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.CrisisCloseNotAllowed);
    }

    [Fact]
    public void Close_needs_a_reason_and_is_final()
    {
        var evt = Critical();
        var noReason = () => evt.Close(Guid.NewGuid(), AppRoles.Supervisor, " ", _clock);
        noReason.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.ReasonRequired);

        evt.Close(Guid.NewGuid(), AppRoles.Supervisor, "Patient reached, safety plan reviewed", _clock);
        evt.Status.Should().Be(CrisisEventStatus.Closed);
        var ack = () => evt.Acknowledge(Guid.NewGuid(), _clock);
        ack.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.CrisisClosed);
    }

    [Fact]
    public void Once_taken_only_the_owner_a_supervisor_or_the_lead_can_act()
    {
        var evt = Critical();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        evt.CanAct(other, AppRoles.Therapist).Should().BeTrue("nobody owns it yet, so nothing urgent waits");

        evt.Acknowledge(owner, _clock);
        evt.CanAct(owner, AppRoles.Therapist).Should().BeTrue();
        evt.CanAct(Guid.NewGuid(), AppRoles.Supervisor).Should().BeTrue();
        evt.CanAct(Guid.NewGuid(), AppRoles.ClinicalLead).Should().BeTrue();
        var act = () => evt.EnsureCanAct(other, AppRoles.Therapist);
        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.CrisisNotOwner);
    }

    [Fact]
    public void Contact_needs_an_owner_and_keeps_the_event_owned()
    {
        var evt = Critical();
        var early = () => evt.MarkInContact();
        early.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.CrisisNotAcknowledged);

        evt.Acknowledge(Guid.NewGuid(), _clock);
        evt.MarkInContact();
        evt.Status.Should().Be(CrisisEventStatus.InContact);
        evt.IsOwned.Should().BeTrue();
        var again = () => evt.Acknowledge(Guid.NewGuid(), _clock);
        again.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.CrisisAlreadyAcknowledged);
    }

    [Fact]
    public void A_handover_moves_ownership_only_when_the_colleague_accepts()
    {
        var evt = Critical();
        var owner = Guid.NewGuid();
        var colleague = Guid.NewGuid();
        evt.Acknowledge(owner, _clock);
        evt.RequestHandover(owner, colleague, _clock);
        evt.AcknowledgedByUserId.Should().Be(owner);
        evt.HandoverToUserId.Should().Be(colleague);

        var wrong = () => evt.AcceptHandover(Guid.NewGuid());
        wrong.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.CrisisNothingPending);

        evt.AcceptHandover(colleague);
        evt.AcknowledgedByUserId.Should().Be(colleague);
        evt.HandoverToUserId.Should().BeNull();
        evt.Status.Should().Be(CrisisEventStatus.Acknowledged);

        evt.RequestHandover(colleague, owner, _clock);
        evt.CancelHandover(owner, AppRoles.Therapist);
        evt.AcknowledgedByUserId.Should().Be(colleague, "a declined offer changes nothing");
    }

    [Fact]
    public void Unreachable_from_a_supervisor_waits_for_the_lead_who_closes_it_with_a_72_hour_review()
    {
        var evt = Critical();
        var supervisor = Guid.NewGuid();
        evt.Close(supervisor, AppRoles.Supervisor, CrisisEvent.UnreachableOutcome, "Three calls, no answer, no location", _clock).Should().BeFalse();
        evt.Status.Should().Be(CrisisEventStatus.Open);
        evt.PendingCloseOutcome.Should().Be(CrisisEvent.UnreachableOutcome);

        var notLead = () => evt.CoSignClose(Guid.NewGuid(), AppRoles.Supervisor, _clock);
        notLead.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.CrisisCloseNotAllowed);

        var lead = Guid.NewGuid();
        evt.CoSignClose(lead, AppRoles.ClinicalLead, _clock);
        evt.Status.Should().Be(CrisisEventStatus.Closed);
        evt.CloseOutcome.Should().Be(CrisisEvent.UnreachableOutcome);
        evt.ClosedByUserId.Should().Be(supervisor);
        evt.CoSignedByUserId.Should().Be(lead);
        evt.ReviewDueAtUtc.Should().Be(_clock.UtcNow.AddHours(72));
        evt.PendingCloseOutcome.Should().BeNull();

        var other = Critical();
        other.Close(Guid.NewGuid(), AppRoles.Supervisor, "safe_contacted", "Reached and safe", _clock).Should().BeTrue();
        other.ReviewDueAtUtc.Should().BeNull();
    }
}

public class CareAssignmentTests
{
    private readonly TestClock _clock = new(DateTimeOffset.UnixEpoch);

    [Fact]
    public void End_requires_a_reason_and_happens_once()
    {
        var a = CareAssignment.Start(Guid.NewGuid(), Guid.NewGuid(), _clock);
        a.IsActive.Should().BeTrue();
        var noReason = () => a.End("", _clock);
        noReason.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.ReasonRequired);
        a.End("Transferred", _clock);
        a.IsActive.Should().BeFalse();
        var twice = () => a.End("again", _clock);
        twice.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.AssignmentAlreadyEnded);
    }

    [Fact]
    public void A_user_cannot_be_their_own_therapist()
    {
        var id = Guid.NewGuid();
        var act = () => CareAssignment.Start(id, id, _clock);
        act.Should().Throw<ArgumentException>();
    }
}

public class AiCallRecordTests
{
    [Fact]
    public void Records_metadata_only_and_normalizes_language()
    {
        var record = AiCallRecord.Start("P-AB12-CD34", "intake.reply", "intake-v7", "model-2026-01", "ar-SA", new TestClock(DateTimeOffset.UnixEpoch));
        record.Language.Should().Be("ar");
        record.Complete(AiCallOutcome.Succeeded, 812, 420, 96);
        record.LatencyMs.Should().Be(812);
        typeof(AiCallRecord).GetProperties().Select(p => p.Name).Should().NotContain(n => n.Contains("Text", StringComparison.Ordinal) || n.Contains("Prompt", StringComparison.Ordinal) && n != "PromptVersion");
    }

    [Fact]
    public void Rejects_real_identifiers_in_place_of_a_pseudonym()
    {
        var act = () => AiCallRecord.Start(Guid.NewGuid().ToString(), "intake.reply", "v1", "m", "ar", new TestClock(DateTimeOffset.UnixEpoch));
        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.InvalidPseudonym);
    }
}
