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
