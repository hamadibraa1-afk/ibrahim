using FieldAttendance.Domain.Attendance;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Geo;
using Xunit;
using static FieldAttendance.Domain.Tests.Builders;
using static FieldAttendance.Domain.Tests.TestTime;

namespace FieldAttendance.Domain.Tests;

public class AttendanceRecordTests
{
    private static readonly GeoPoint Here = new(25.3463, 55.4209);

    private static AttendanceCalculationResult Recalculate(AttendanceRecord r) =>
        AttendanceCalculator.Calculate(r.BuildCalculationInput([], [], 0));

    [Fact]
    public void Case05_ForgottenCheckout_AutoClosesAtShiftEnd_NoOvertime()
    {
        var r = Record();
        r.CheckIn(T(8, 0), Here, 10, 5, CheckInType.Normal);

        Assert.False(r.AutoClose(T(16, 59), delayMinutes: 60));
        Assert.True(r.AutoClose(T(17, 0), delayMinutes: 60));
        Assert.Equal(T(16, 0), r.CheckOutAt);
        Assert.Equal(CheckOutType.Auto, r.CheckOutType);
        Assert.Equal(0, Recalculate(r).OvertimeMinutes);
    }

    [Fact]
    public void AutoClose_IsIdempotent()
    {
        var r = Record();
        r.CheckIn(T(8, 0), Here, 10, 5, CheckInType.Normal);
        Assert.True(r.AutoClose(T(17, 0), 60));
        Assert.False(r.AutoClose(T(17, 5), 60));
    }

    [Fact]
    public void AutoClose_WithOpenExit_ClosesAtExitTime()
    {
        var r = Record();
        r.CheckIn(T(8, 0), Here, 10, 5, CheckInType.Normal);
        r.StartTemporaryExit(T(11, 0), Guid.NewGuid(), Span(11, 0, 12, 0));
        Assert.True(r.AutoClose(T(17, 0), 60));
        Assert.Equal(T(11, 0), r.CheckOutAt);
        Assert.Equal(CheckOutType.UnreturnedExit, r.CheckOutType);
    }

    [Fact]
    public void Case10_NoCheckInByShiftEnd_IsAbsent()
    {
        var r = Record();
        Assert.False(r.MarkAbsentIfDue(T(15, 59), hasPendingException: false));
        Assert.False(r.MarkAbsentIfDue(T(16, 0), hasPendingException: true));
        Assert.True(r.MarkAbsentIfDue(T(16, 0), hasPendingException: false));
        Assert.Equal(AttendanceStatus.Absent, r.Status);
        Assert.False(r.MarkAbsentIfDue(T(16, 5), false));
    }

    [Fact]
    public void ApprovedException_AfterAbsence_RestoresPresence()
    {
        var r = Record();
        r.MarkAbsentIfDue(T(16, 0), false);
        r.CheckIn(T(8, 5), Here, 10, 300, CheckInType.Exception); // requested at 08:05, approved later
        Assert.Equal(AttendanceStatus.Present, r.Status);
    }

    [Fact]
    public void EarlyCheckIn_IsAllowedInsideTheShiftsWindow()
    {
        var r = Record(); // window is 60 minutes before 08:00
        r.CheckIn(T(7, 15), Here, 10, 5, CheckInType.Normal);
        Assert.Equal(T(7, 15), r.CheckInAt);
    }

    [Fact]
    public void EarlyCheckIn_BeforeTheWindow_IsRejected() =>
        Assert.Throws<DomainException>(() => Record().CheckIn(T(6, 30), Here, 10, 5, CheckInType.Normal));

    [Fact]
    public void EarlyArrival_DoesNotInflateWorkedHours()
    {
        var r = Record();
        r.CheckIn(T(7, 15), Here, 10, 5, CheckInType.Normal);
        r.CheckOut(T(16, 0), Here, CheckOutType.Normal);
        var result = Recalculate(r);
        Assert.Equal(480, result.NetWorkMinutes); // counted from 08:00, not 07:15
        Assert.Equal(0, result.OvertimeMinutes);
    }

    [Fact]
    public void DoubleCheckIn_IsRejected()
    {
        var r = Record();
        r.CheckIn(T(8, 0), Here, 10, 5, CheckInType.Normal);
        Assert.Throws<DomainException>(() => r.CheckIn(T(8, 0), Here, 10, 5, CheckInType.Normal));
    }

    [Fact]
    public void CheckIn_AfterShiftEnd_IsRejected() =>
        Assert.Throws<DomainException>(() => Record().CheckIn(T(16, 0), Here, 10, 5, CheckInType.Normal));

    [Fact]
    public void CheckIn_OnLeave_IsRejected() =>
        Assert.Throws<DomainException>(() => Record(onLeave: true).CheckIn(T(8, 0), Here, 10, 5, CheckInType.Normal));

    [Fact]
    public void Checkout_DuringOpenExit_RequiresReturnFirst()
    {
        var r = Record();
        r.CheckIn(T(8, 0), Here, 10, 5, CheckInType.Normal);
        r.StartTemporaryExit(T(11, 0), Guid.NewGuid(), Span(11, 0, 12, 0));
        Assert.Throws<DomainException>(() => r.CheckOut(T(11, 30), Here, CheckOutType.Normal));
        r.ReturnFromExit(T(11, 50), Here);
        r.CheckOut(T(16, 0), Here, CheckOutType.Normal);
        Assert.Equal(50, Recalculate(r).PermissionMinutes);
    }

    [Fact]
    public void TemporaryExit_OutsidePermissionWindow_IsRejected()
    {
        var r = Record();
        r.CheckIn(T(8, 0), Here, 10, 5, CheckInType.Normal);
        Assert.Throws<DomainException>(() => r.StartTemporaryExit(T(10, 30), Guid.NewGuid(), Span(11, 0, 12, 0)));
    }

    [Fact]
    public void SamePermission_CannotBeUsedTwice()
    {
        var r = Record();
        var permission = Guid.NewGuid();
        r.CheckIn(T(8, 0), Here, 10, 5, CheckInType.Normal);
        r.StartTemporaryExit(T(11, 0), permission, Span(11, 0, 12, 0));
        r.ReturnFromExit(T(11, 20), Here);
        Assert.Throws<DomainException>(() => r.StartTemporaryExit(T(11, 30), permission, Span(11, 0, 12, 0)));
    }

    [Fact]
    public void SystemCheckoutTypes_CannotBeSetManually()
    {
        var r = Record();
        r.CheckIn(T(8, 0), Here, 10, 5, CheckInType.Normal);
        Assert.Throws<DomainException>(() => r.CheckOut(T(16, 0), Here, CheckOutType.Auto));
    }

    [Fact]
    public void Reschedule_AfterCheckIn_IsRejected()
    {
        var r = Record();
        r.CheckIn(T(8, 0), Here, 10, 5, CheckInType.Normal);
        var t = Evening();
        var moved = new Scheduling.ResolvedShift(Employee, Day0, LocationB, t.Id, t.WindowFor(Day0), 0, 10, false, 0,
            Scheduling.ShiftSource.Override, Guid.NewGuid(), OverrideType.TemporaryTransfer, false);
        Assert.Throws<DomainException>(() => r.ApplySchedule(moved));
    }

    [Fact]
    public void SelectRecordForCheckIn_PicksEarliestShiftNotEnded()
    {
        var morning = Record(Morning());
        var evening = Record(Evening());
        Assert.Same(morning, AttendancePolicies.SelectRecordForCheckIn([evening, morning], T(7, 30)));
        Assert.Same(evening, AttendancePolicies.SelectRecordForCheckIn([evening, morning], T(16, 30)));
        Assert.Null(AttendancePolicies.SelectRecordForCheckIn([evening, morning], T(22, 0)));
    }

    [Fact]
    public void NotArrivedAlert_WaitsForGraceAndDelay()
    {
        Assert.Equal(T(8, 25), AttendancePolicies.NotArrivedAlertDueAt(Record(), [], alertDelayMinutes: 15));
    }

    [Fact]
    public void NotArrivedAlert_WithLatePermission_WaitsUntilPermissionEnds() =>
        Assert.Equal(T(9, 15), AttendancePolicies.NotArrivedAlertDueAt(Record(), [Span(8, 0, 9, 0)], 15));

    [Fact]
    public void NotArrivedAlert_NoneAfterCheckIn()
    {
        var r = Record();
        r.CheckIn(T(8, 0), Here, 10, 5, CheckInType.Normal);
        Assert.Null(AttendancePolicies.NotArrivedAlertDueAt(r, [], 15));
    }
}
