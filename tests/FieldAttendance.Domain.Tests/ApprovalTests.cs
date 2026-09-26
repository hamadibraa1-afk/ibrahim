using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using Xunit;
using static FieldAttendance.Domain.Tests.TestTime;

namespace FieldAttendance.Domain.Tests;

public class ApprovalFlowTests
{
    private static ApprovalFlow Flow()
    {
        var flow = new ApprovalFlow(RequestKind.Leave, "مسار الإجازات", "Leave flow", 48);
        flow.SetLevels([ApprovalStage.SectionHead, ApprovalStage.DepartmentManager, ApprovalStage.Hr]);
        return flow;
    }

    [Fact]
    public void AChain_KeepsTheOrderItWasGiven()
    {
        var stages = Flow().Stages();
        Assert.Equal(3, stages.Count);
        Assert.Equal(ApprovalStage.SectionHead, stages[0]);
        Assert.Equal(ApprovalStage.DepartmentManager, stages[1]);
        Assert.Equal(ApprovalStage.Hr, stages[2]);
    }

    [Fact]
    public void Reordering_ReplacesTheChainInsteadOfAppending()
    {
        var flow = Flow();
        flow.SetLevels([ApprovalStage.LineManager, ApprovalStage.Hr]);
        Assert.Equal(2, flow.Levels.Count);
        Assert.Equal(ApprovalStage.LineManager, flow.Stages()[0]);
    }

    [Fact]
    public void AnEmptyChain_IsRejected() =>
        Assert.Throws<DomainException>(() => Flow().SetLevels([]));

    [Fact]
    public void TheSameLevelTwice_IsRejected() =>
        Assert.Throws<DomainException>(() => Flow().SetLevels([ApprovalStage.Hr, ApprovalStage.Hr]));
}

public class ApprovalStepTests
{
    private static readonly Guid Employee = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly Guid Request = Guid.NewGuid();

    private static ApprovalStep Step(int order = 1, Guid? approver = null) =>
        new(RequestKind.Leave, Request, Employee, order, ApprovalStage.DepartmentManager, approver ?? Manager);

    [Fact]
    public void ANewStep_WaitsForItsApprover()
    {
        var step = Step();
        Assert.True(step.IsOpen);
        Assert.Equal(Manager, step.ApproverId);
    }

    [Fact]
    public void ADecidedStep_CannotBeDecidedAgain()
    {
        var step = Step();
        step.Approve(Manager, T(9, 0), "موافق");
        Assert.Throws<DomainException>(() => step.Reject(Manager, T(9, 5), "تراجعت"));
    }

    [Fact]
    public void Rejection_RequiresAReason() =>
        Assert.Throws<DomainException>(() => Step().Reject(Manager, T(9, 0), " "));

    [Fact]
    public void AStepWithNobodyInTheRole_IsSkipped()
    {
        var step = Step(approver: null);
        step.Skip("لا يوجد رئيس قسم");
        Assert.Equal(ApprovalStepStatus.Skipped, step.Status);
    }

    [Fact]
    public void AStepCanBeReassigned_WhileItIsStillOpen()
    {
        var step = Step();
        var deputy = Guid.NewGuid();
        step.Reassign(deputy);
        Assert.Equal(deputy, step.ApproverId);

        step.Approve(deputy, T(9, 0), null);
        Assert.Throws<DomainException>(() => step.Reassign(Guid.NewGuid()));
    }
}

public class ReturnToWorkTests
{
    private static readonly Guid Employee = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly DateOnly Expected = new(2026, 9, 20);

    private static ReturnToWork Return() => new(Employee, Guid.NewGuid(), Expected);

    [Fact]
    public void ReportingOnTime_AwaitsConfirmation()
    {
        var r = Return();
        r.Report(Expected, null);
        Assert.Equal(ReturnStatus.Submitted, r.Status);
        Assert.Equal(0, r.LateDays);
    }

    [Fact]
    public void ReportingLate_IsFlaggedWithTheNumberOfDays()
    {
        var r = Return();
        r.Report(Expected.AddDays(3), "تأخر السفر");
        Assert.Equal(ReturnStatus.Late, r.Status);
        Assert.Equal(3, r.LateDays);
    }

    [Fact]
    public void ConfirmationNeedsAReportFirst() =>
        Assert.Throws<DomainException>(() => Return().Confirm(Manager, T(9, 0)));

    [Fact]
    public void AConfirmedReturn_CannotBeRewritten()
    {
        var r = Return();
        r.Report(Expected, null);
        r.Confirm(Manager, T(9, 0));
        Assert.Throws<DomainException>(() => r.Report(Expected.AddDays(1), null));
    }

    [Fact]
    public void NoReportAfterTheExpectedDay_ShowsAsOverdue()
    {
        var r = Return();
        Assert.False(r.IsOverdue(Expected));
        Assert.True(r.IsOverdue(Expected.AddDays(1)));
    }
}

public class OrgTimeTests
{
    [Fact]
    public void LocalTime_BecomesAnExactInstant()
    {
        var at = FieldAttendance.Domain.Time.OrgTime.At(new DateOnly(2026, 9, 24), new TimeOnly(8, 0));
        Assert.Equal(new DateOnly(2026, 9, 24), FieldAttendance.Domain.Time.OrgTime.DateOf(at));
        Assert.Equal(new TimeOnly(8, 0), FieldAttendance.Domain.Time.OrgTime.TimeOf(at));
    }

    [Fact]
    public void StorageIsUtc_ButTheLocalDayIsUnchanged()
    {
        var at = FieldAttendance.Domain.Time.OrgTime.At(new DateOnly(2026, 9, 24), new TimeOnly(1, 30));
        var stored = FieldAttendance.Domain.Time.OrgTime.ToStorage(at);

        Assert.Equal(TimeSpan.Zero, stored.Offset);
        Assert.Equal(at.UtcDateTime, stored.UtcDateTime);
        // 01:30 local is still the 24th locally even though it is the 23rd in UTC.
        Assert.Equal(new DateOnly(2026, 9, 24), FieldAttendance.Domain.Time.OrgTime.DateOf(stored));
    }

    [Fact]
    public void AnUnknownZone_IsIgnoredInsteadOfBreakingStartup()
    {
        var before = FieldAttendance.Domain.Time.OrgTime.Zone;
        Assert.False(FieldAttendance.Domain.Time.OrgTime.Configure("Mars/Olympus"));
        Assert.Same(before, FieldAttendance.Domain.Time.OrgTime.Zone);
    }

    [Fact]
    public void SecondsAreTruncated_SoTheGraceBoundaryIsNotMissedByOneSecond()
    {
        var value = FieldAttendance.Domain.Time.OrgTime.At(new DateOnly(2026, 9, 24), new TimeOnly(8, 10)).AddSeconds(59);
        Assert.Equal(new TimeOnly(8, 10), FieldAttendance.Domain.Time.OrgTime.TimeOf(FieldAttendance.Domain.Time.OrgTime.TruncateToMinute(value)));
    }
}

public class NotificationTests
{
    private static readonly Guid Recipient = Guid.NewGuid();

    [Fact]
    public void TheSameFact_ProducesTheSameKey()
    {
        var first = Notification.KeyFor(NotificationKind.ReturnOverdue, Recipient, "2026-09-24", "emp-1");
        var second = Notification.KeyFor(NotificationKind.ReturnOverdue, Recipient, "2026-09-24", "emp-1");
        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentDays_ProduceDifferentKeys()
    {
        var monday = Notification.KeyFor(NotificationKind.LocationUncovered, Recipient, "2026-09-24");
        var tuesday = Notification.KeyFor(NotificationKind.LocationUncovered, Recipient, "2026-09-25");
        Assert.NotEqual(monday, tuesday);
    }

    [Fact]
    public void ReadingTwice_KeepsTheFirstTime()
    {
        var n = new Notification(Recipient, NotificationKind.RequestDecided, "k", T(9, 0));
        Assert.True(n.IsUnread);
        n.MarkRead(T(10, 0));
        n.MarkRead(T(11, 0));
        Assert.Equal(T(10, 0), n.ReadAt);
        Assert.False(n.IsUnread);
    }

    [Fact]
    public void ChannelsAreIndependent()
    {
        var setting = new NotificationSetting(NotificationKind.PayrollBlocked, inApp: true, email: false, sms: true);
        Assert.True(setting.Uses(NotificationChannel.InApp));
        Assert.False(setting.Uses(NotificationChannel.Email));
        Assert.True(setting.Uses(NotificationChannel.Sms));
    }
}
