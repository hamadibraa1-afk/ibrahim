using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Time;
using Xunit;
using static FieldAttendance.Domain.Tests.Builders;
using static FieldAttendance.Domain.Tests.TestTime;

namespace FieldAttendance.Domain.Tests;

public class PermissionAndLeaveTests
{
    private static readonly Guid Supervisor = Guid.NewGuid();

    [Fact]
    public void LatePermission_RunsFromShiftStartToUntil()
    {
        var p = PermissionRequest.Late(Employee, Day0, new TimeOnly(9, 0), StandardShift, "Hospital", Employee);
        Assert.Equal(Span(8, 0, 9, 0), p.ToInterval(StandardShift));
    }

    [Fact]
    public void EarlyDeparture_RunsFromFromToShiftEnd()
    {
        var p = PermissionRequest.EarlyDeparture(Employee, Day0, new TimeOnly(14, 0), StandardShift, "School pickup", Employee);
        Assert.Equal(Span(14, 0, 16, 0), p.ToInterval(StandardShift));
    }

    [Fact]
    public void PermissionOutsideShift_IsRejected() =>
        Assert.Throws<DomainException>(() =>
            PermissionRequest.EarlyDeparture(Employee, Day0, new TimeOnly(17, 0), StandardShift, "x", Employee));

    [Fact]
    public void TemporaryExit_EndMustBeAfterStart() =>
        Assert.Throws<DomainException>(() =>
            PermissionRequest.TemporaryExit(Employee, Day0, new TimeOnly(12, 0), new TimeOnly(11, 0), StandardShift, "x", Employee));

    [Fact]
    public void PermissionReason_IsRequired() =>
        Assert.Throws<DomainException>(() =>
            PermissionRequest.Late(Employee, Day0, new TimeOnly(9, 0), StandardShift, " ", Employee));

    [Fact]
    public void NightShift_EarlyDepartureMapsToNextMorning()
    {
        var night = ShiftTiming.Window(Day0, new TimeOnly(22, 0), new TimeOnly(6, 0));
        var p = PermissionRequest.EarlyDeparture(Employee, Day0, new TimeOnly(4, 0), night, "x", Employee);
        Assert.Equal(T(4, 0, dayOffset: 1), p.ToInterval(night).Start);
    }

    [Fact]
    public void Decision_IsFinal()
    {
        var p = PermissionRequest.Late(Employee, Day0, new TimeOnly(9, 0), StandardShift, "x", Employee);
        p.Approve(Supervisor, T(7, 0));
        Assert.Throws<DomainException>(() => p.Approve(Guid.NewGuid(), T(7, 1)));
        Assert.Throws<DomainException>(() => p.Reject(Guid.NewGuid(), T(7, 1), "late"));
    }

    [Fact]
    public void Reject_RequiresReason()
    {
        var p = PermissionRequest.Late(Employee, Day0, new TimeOnly(9, 0), StandardShift, "x", Employee);
        Assert.Throws<DomainException>(() => p.Reject(Supervisor, T(7, 0), ""));
        Assert.Equal(RequestStatus.Pending, p.Status);
    }

    [Fact]
    public void ApprovedPermission_CannotBeCancelledOnceStarted()
    {
        var p = PermissionRequest.EarlyDeparture(Employee, Day0, new TimeOnly(14, 0), StandardShift, "x", Employee);
        p.Approve(Supervisor, T(7, 0));
        Assert.Throws<DomainException>(() => p.Cancel(T(14, 0), StandardShift));
    }

    [Fact]
    public void ApprovedPermission_CanBeCancelledBeforeStart()
    {
        var p = PermissionRequest.EarlyDeparture(Employee, Day0, new TimeOnly(14, 0), StandardShift, "x", Employee);
        p.Approve(Supervisor, T(7, 0));
        p.Cancel(T(13, 0), StandardShift);
        Assert.Equal(RequestStatus.Cancelled, p.Status);
    }

    [Fact]
    public void OnBehalfSubmission_IsTracked()
    {
        var p = PermissionRequest.Late(Employee, Day0, new TimeOnly(9, 0), StandardShift, "x", Supervisor);
        Assert.True(p.IsSubmittedOnBehalf);
    }

    [Fact]
    public void Leave_WithNoWorkingDays_IsRejected() =>
        Assert.Throws<DomainException>(() => new LeaveRequest(Employee, Guid.NewGuid(), Day0.AddDays(5), Day0.AddDays(6), 0, null, Employee));

    [Fact]
    public void LeaveBalance_CannotGoNegative_AndRestores()
    {
        var balance = new LeaveBalance(Employee, Guid.NewGuid(), 2026, 5);
        balance.Deduct(5);
        Assert.Throws<DomainException>(() => balance.Deduct(1));
        balance.Restore(2); // used 5 → 3
        Assert.Equal(2, balance.RemainingDays);
    }

    [Fact]
    public void UnlimitedLeaveType_HasNoBalanceLimit()
    {
        var balance = new LeaveBalance(Employee, Guid.NewGuid(), 2026, null);
        balance.Deduct(30);
        Assert.Null(balance.RemainingDays);
    }
}
