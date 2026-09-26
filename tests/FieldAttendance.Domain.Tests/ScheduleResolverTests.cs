using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Scheduling;
using Xunit;
using static FieldAttendance.Domain.Tests.Builders;
using static FieldAttendance.Domain.Tests.TestTime;

namespace FieldAttendance.Domain.Tests;

public class ScheduleResolverTests
{
    private readonly ShiftTemplate _morning = Morning();

    private Assignment BaseAssignment(DateOnly? end = null) =>
        new(Employee, LocationA, _morning.Id, Day0, end, SundayToThursday, null);

    [Fact]
    public void BaseAssignment_OnWorkingDay_IsResolved()
    {
        var shift = Assert.Single(ScheduleResolver.Resolve(Employee, Day0, Snapshot([_morning], [BaseAssignment()])));
        Assert.Equal(LocationA, shift.LocationId);
        Assert.Equal(T(8, 0), shift.Window.Start);
        Assert.Equal(ShiftSource.BaseAssignment, shift.Source);
    }

    [Fact]
    public void BaseAssignment_OnRestDay_IsEmpty() =>
        Assert.Empty(ScheduleResolver.Resolve(Employee, Day0.AddDays(5), Snapshot([_morning], [BaseAssignment()]))); // Friday

    [Fact]
    public void BaseAssignment_AfterEndDate_IsEmpty() =>
        Assert.Empty(ScheduleResolver.Resolve(Employee, Day0.AddDays(2), Snapshot([_morning], [BaseAssignment(Day0.AddDays(1))])));

    [Fact]
    public void DeactivatedAssignment_IsIgnored()
    {
        var a = BaseAssignment();
        a.Deactivate();
        Assert.Empty(ScheduleResolver.Resolve(Employee, Day0, Snapshot([_morning], [a])));
    }

    [Fact]
    public void TemporaryTransfer_ReplacesBaseForTheDay()
    {
        var transfer = new AssignmentOverride(Employee, Day0, Day0, OverrideType.TemporaryTransfer, LocationB, _morning.Id, null, "Busy branch");
        var shift = Assert.Single(ScheduleResolver.Resolve(Employee, Day0, Snapshot([_morning], [BaseAssignment()], [transfer])));
        Assert.Equal(LocationB, shift.LocationId);
        Assert.Equal(ShiftSource.Override, shift.Source);
    }

    [Fact]
    public void TemporaryTransfer_EndsAutomatically_BaseResumes()
    {
        var transfer = new AssignmentOverride(Employee, Day0, Day0, OverrideType.TemporaryTransfer, LocationB, _morning.Id, null, null);
        var next = Assert.Single(ScheduleResolver.Resolve(Employee, Day0.AddDays(1), Snapshot([_morning], [BaseAssignment()], [transfer])));
        Assert.Equal(LocationA, next.LocationId);
    }

    [Fact]
    public void Cancel_RemovesTheDay()
    {
        var cancel = new AssignmentOverride(Employee, Day0, Day0, OverrideType.Cancel, null, null, null, "Replaced");
        Assert.Empty(ScheduleResolver.Resolve(Employee, Day0, Snapshot([_morning], [BaseAssignment()], [cancel])));
    }

    [Fact]
    public void CoverCreatedAfterCancel_Wins()
    {
        var cancel = new AssignmentOverride(Employee, Day0, Day0, OverrideType.Cancel, null, null, null, null).Created(1);
        var cover = new AssignmentOverride(Employee, Day0, Day0, OverrideType.EmergencyCover, LocationB, _morning.Id, null, null).Created(2);
        var shift = Assert.Single(ScheduleResolver.Resolve(Employee, Day0, Snapshot([_morning], [BaseAssignment()], [cover, cancel])));
        Assert.Equal(LocationB, shift.LocationId);
    }

    [Fact]
    public void CancelCreatedAfterCover_Wins()
    {
        var cover = new AssignmentOverride(Employee, Day0, Day0, OverrideType.EmergencyCover, LocationB, _morning.Id, null, null).Created(1);
        var cancel = new AssignmentOverride(Employee, Day0, Day0, OverrideType.Cancel, null, null, null, null).Created(2);
        Assert.Empty(ScheduleResolver.Resolve(Employee, Day0, Snapshot([_morning], [BaseAssignment()], [cover, cancel])));
    }

    [Fact]
    public void ApprovedLeave_FlagsShiftAsOnLeave_PendingDoesNot()
    {
        var approved = new LeaveRequest(Employee, Guid.NewGuid(), Day0, Day0, 1, null, Employee);
        approved.Approve(Guid.NewGuid(), T(7, 0));
        var pending = new LeaveRequest(Employee, Guid.NewGuid(), Day0.AddDays(1), Day0.AddDays(1), 1, null, Employee);
        var snapshot = Snapshot([_morning], [BaseAssignment()], leaves: [approved, pending]);

        Assert.True(Assert.Single(ScheduleResolver.Resolve(Employee, Day0, snapshot)).IsOnLeave);
        Assert.False(Assert.Single(ScheduleResolver.Resolve(Employee, Day0.AddDays(1), snapshot)).IsOnLeave);
    }

    [Fact]
    public void LeaveDays_CountOnlyScheduledWorkingDays()
    {
        // Sunday → Saturday: 5 working days (Sun–Thu), Friday and Saturday are free.
        var days = ScheduleResolver.CountScheduledWorkingDays(Employee, Day0, Day0.AddDays(6), Snapshot([_morning], [BaseAssignment()]));
        Assert.Equal(5, days);
    }

    [Fact]
    public void NightShift_EndsNextDay()
    {
        var night = Night();
        var a = new Assignment(Employee, LocationA, night.Id, Day0, null, WorkDays.All, null);
        var shift = Assert.Single(ScheduleResolver.Resolve(Employee, Day0, Snapshot([night], [a])));
        Assert.Equal(T(6, 0, dayOffset: 1), shift.Window.End);
    }

    [Fact]
    public void MissingShiftTemplate_FailsLoudly()
    {
        var a = new Assignment(Employee, LocationA, Guid.NewGuid(), Day0, null, WorkDays.All, null);
        Assert.Throws<DomainException>(() => ScheduleResolver.Resolve(Employee, Day0, Snapshot([_morning], [a])));
    }

    [Fact]
    public void OverrideWithoutShift_IsRejectedAtCreation() =>
        Assert.Throws<DomainException>(() =>
            new AssignmentOverride(Employee, Day0, Day0, OverrideType.EmergencyCover, LocationB, null, null, null));

    [Fact]
    public void Replacement_RequiresReplacedEmployee_AndNotSelf()
    {
        var shift = _morning.Id;
        Assert.Throws<DomainException>(() => new AssignmentOverride(Employee, Day0, Day0, OverrideType.Replacement, LocationA, shift, null, null));
        Assert.Throws<DomainException>(() => new AssignmentOverride(Employee, Day0, Day0, OverrideType.Replacement, LocationA, shift, Employee, null));
    }
}
