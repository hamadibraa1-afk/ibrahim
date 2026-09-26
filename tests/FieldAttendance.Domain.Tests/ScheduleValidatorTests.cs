using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Scheduling;
using Xunit;
using static FieldAttendance.Domain.Tests.Builders;
using static FieldAttendance.Domain.Tests.TestTime;

namespace FieldAttendance.Domain.Tests;

public class ScheduleValidatorTests
{
    private static ResolvedShift Shift(Guid employee, Guid location, ShiftTemplate t, int dayOffset = 0, bool onLeave = false) =>
        new(employee, Day0.AddDays(dayOffset), location, t.Id, t.WindowFor(Day0.AddDays(dayOffset)), 0, 10, false, 0,
            ShiftSource.BaseAssignment, Guid.NewGuid(), null, onLeave);

    [Fact]
    public void SameEmployee_OverlappingShifts_AreConflicts()
    {
        var morning = Morning();
        var custom = new ShiftTemplate("x", "x", new TimeOnly(12, 0), new TimeOnly(18, 0), 0, 0, false);
        Assert.Single(ScheduleValidator.FindEmployeeOverlaps([Shift(Employee, LocationA, morning), Shift(Employee, LocationB, custom)]));
    }

    [Fact]
    public void BackToBackShifts_AreNotConflicts() =>
        Assert.Empty(ScheduleValidator.FindEmployeeOverlaps([Shift(Employee, LocationA, Morning()), Shift(Employee, LocationA, Evening())]));

    [Fact]
    public void NightShift_OverlapsNextMorning()
    {
        var early = new ShiftTemplate("x", "x", new TimeOnly(5, 0), new TimeOnly(13, 0), 0, 0, false);
        Assert.Single(ScheduleValidator.FindEmployeeOverlaps([Shift(Employee, LocationA, Night()), Shift(Employee, LocationA, early, dayOffset: 1)]));
    }

    [Fact]
    public void DifferentEmployees_SameTime_AreNotConflicts() =>
        Assert.Empty(ScheduleValidator.FindEmployeeOverlaps([Shift(Employee, LocationA, Morning()), Shift(OtherEmployee, LocationA, Morning())]));

    [Fact]
    public void Capacity_Exceeded_ReportsTheInterval()
    {
        var v = Assert.Single(ScheduleValidator.FindCapacityViolations(LocationA, 1,
            [Shift(Employee, LocationA, Morning()), Shift(OtherEmployee, LocationA, Morning())]));
        Assert.Equal(2, v.Assigned);
        Assert.Equal(480, v.During.Minutes);
    }

    [Fact]
    public void Capacity_WithinLimit_IsFine() =>
        Assert.Empty(ScheduleValidator.FindCapacityViolations(LocationA, 2,
            [Shift(Employee, LocationA, Morning()), Shift(OtherEmployee, LocationA, Morning())]));

    [Fact]
    public void Capacity_BackToBack_IsFine() =>
        Assert.Empty(ScheduleValidator.FindCapacityViolations(LocationA, 1,
            [Shift(Employee, LocationA, Morning()), Shift(OtherEmployee, LocationA, Evening())]));

    [Fact]
    public void Capacity_IgnoresEmployeesOnLeave() =>
        Assert.Empty(ScheduleValidator.FindCapacityViolations(LocationA, 1,
            [Shift(Employee, LocationA, Morning(), onLeave: true), Shift(OtherEmployee, LocationA, Morning())]));
}
