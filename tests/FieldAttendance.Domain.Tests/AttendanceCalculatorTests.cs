using FieldAttendance.Domain.Attendance;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Time;
using Xunit;
using static FieldAttendance.Domain.Tests.TestTime;

namespace FieldAttendance.Domain.Tests;

public class AttendanceCalculatorTests
{
    private static AttendanceCalculationResult Calc(
        DateTimeOffset? checkIn,
        DateTimeOffset? checkOut,
        CheckOutType? type = CheckOutType.Normal,
        TimeInterval? shift = null,
        int grace = 10,
        int breakMinutes = 0,
        bool countEarly = false,
        int minOvertime = 0,
        ExitSpan[]? exits = null,
        TimeInterval[]? late = null,
        TimeInterval[]? early = null) =>
        AttendanceCalculator.Calculate(new AttendanceCalculationInput(
            shift ?? StandardShift, breakMinutes, grace, countEarly, minOvertime,
            checkIn, checkOut, checkOut is null ? null : type,
            exits ?? [], late ?? [], early ?? []));

    // ---- Spec 11.3 acceptance cases ----

    [Fact]
    public void Case01_CheckInWithinGrace_IsNotLate() =>
        Assert.Equal(0, Calc(T(8, 7), T(16, 0)).LateTotal);

    [Fact]
    public void Case02_CheckInAfterGrace_LateCountsFromShiftStart()
    {
        var r = Calc(T(8, 25), T(16, 0));
        Assert.Equal(25, r.LateTotal);
        Assert.Equal(25, r.LateUnexcused);
        Assert.Equal(0, r.LateExcused);
    }

    [Fact]
    public void Case03_EarlyArrival_IsNotOvertimeByDefault() =>
        Assert.Equal(0, Calc(T(7, 40), T(16, 0)).OvertimeMinutes);

    [Fact]
    public void Case03b_EarlyArrival_DoesNotCountAsWorkedTime()
    {
        var r = Calc(T(7, 40), T(16, 0));
        Assert.Equal(480, r.GrossMinutes);
        Assert.Equal(480, r.NetWorkMinutes);
    }

    [Fact]
    public void EarlyArrival_CountsAsWorkedTime_WhenTheShiftPaysForIt()
    {
        var r = Calc(T(7, 40), T(16, 0), countEarly: true);
        Assert.Equal(500, r.GrossMinutes);
        Assert.Equal(20, r.OvertimeMinutes);
    }

    [Fact]
    public void Case04_StayingAfterShiftEnd_IsOvertime() =>
        Assert.Equal(45, Calc(T(8, 0), T(16, 45)).OvertimeMinutes);

    // Case05 (auto check-out) is covered end-to-end in AttendanceRecordTests.

    [Fact]
    public void Case06_LateWithinLatePermission_IsFullyExcused()
    {
        var r = Calc(T(8, 40), T(16, 0), late: [Span(8, 0, 9, 0)]);
        Assert.Equal(40, r.LateTotal);
        Assert.Equal(40, r.LateExcused);
        Assert.Equal(0, r.LateUnexcused);
    }

    [Fact]
    public void Case07_LateBeyondLatePermission_RemainderIsUnexcused()
    {
        var r = Calc(T(9, 15), T(16, 0), late: [Span(8, 0, 9, 0)]);
        Assert.Equal(75, r.LateTotal);
        Assert.Equal(60, r.LateExcused);
        Assert.Equal(15, r.LateUnexcused);
    }

    [Fact]
    public void Case08_LeavingAfterEarlyDeparturePermissionStart_IsFullyExcused()
    {
        var r = Calc(T(8, 0), T(14, 30), early: [Span(14, 0, 16, 0)]);
        Assert.Equal(90, r.EarlyTotal);
        Assert.Equal(90, r.EarlyExcused);
        Assert.Equal(0, r.EarlyUnexcused);
    }

    [Fact]
    public void Case09_LeavingBeforeEarlyDeparturePermissionStart_DifferenceIsUnexcused()
    {
        var r = Calc(T(8, 0), T(13, 30), early: [Span(14, 0, 16, 0)]);
        Assert.Equal(150, r.EarlyTotal);
        Assert.Equal(120, r.EarlyExcused);
        Assert.Equal(30, r.EarlyUnexcused);
    }

    // Case10 (absence) is covered in AttendanceRecordTests.

    [Fact]
    public void Case11_TemporaryExitReturnedLate_PermissionHoursAndLateReturn()
    {
        var exit = new ExitSpan(T(11, 0), T(12, 20), Span(11, 0, 12, 0));
        var r = Calc(T(8, 0), T(16, 0), exits: [exit]);
        Assert.Equal(80, r.PermissionMinutes);
        Assert.Equal(20, r.LateReturnMinutes);
        Assert.Equal(20, r.LateUnexcused);
        Assert.Equal(480, r.GrossMinutes);
        Assert.Equal(400, r.NetWorkMinutes);
    }

    // ---- Edge cases ----

    [Fact]
    public void GraceBoundary_ExactlyAtGraceEnd_IsNotLate() =>
        Assert.Equal(0, Calc(T(8, 10), T(16, 0)).LateTotal);

    [Fact]
    public void GraceBoundary_SecondsAreTruncated_NotLate() =>
        Assert.Equal(0, Calc(T(8, 10, 59), T(16, 0)).LateTotal);

    [Fact]
    public void GraceBoundary_OneMinutePastGrace_CountsFromStart() =>
        Assert.Equal(11, Calc(T(8, 11), T(16, 0)).LateTotal);

    [Fact]
    public void NoCheckIn_ReturnsAllZeros() =>
        Assert.Equal(AttendanceCalculationResult.Empty, Calc(null, null));

    [Fact]
    public void StillCheckedIn_OnlyLatenessIsComputed()
    {
        var r = Calc(T(8, 30), null);
        Assert.Equal(30, r.LateTotal);
        Assert.Equal(0, r.GrossMinutes);
        Assert.Equal(0, r.OvertimeMinutes);
    }

    [Fact]
    public void BreakIsDeductedFromNetWork() =>
        Assert.Equal(420, Calc(T(8, 0), T(16, 0), breakMinutes: 60).NetWorkMinutes);

    [Fact]
    public void NetWorkNeverNegative() =>
        Assert.Equal(0, Calc(T(8, 0), T(8, 30), breakMinutes: 60).NetWorkMinutes);

    [Fact]
    public void OverlappingLatePermissions_AreNotDoubleCounted()
    {
        var r = Calc(T(10, 0), T(16, 0), late: [Span(8, 0, 9, 0), Span(8, 0, 9, 30)]);
        Assert.Equal(120, r.LateTotal);
        Assert.Equal(90, r.LateExcused);
        Assert.Equal(30, r.LateUnexcused);
    }

    [Fact]
    public void EarlyArrivalCountsAsOvertime_WhenEnabled() =>
        Assert.Equal(30, Calc(T(7, 30), T(16, 0), countEarly: true).OvertimeMinutes);

    [Fact]
    public void OvertimeBelowMinimum_IsZero() =>
        Assert.Equal(0, Calc(T(8, 0), T(16, 20), minOvertime: 30).OvertimeMinutes);

    [Fact]
    public void AutoCheckout_HasNoOvertimeAndNoEarlyDeparture()
    {
        var r = Calc(T(8, 0), T(16, 0), CheckOutType.Auto);
        Assert.Equal(0, r.OvertimeMinutes);
        Assert.Equal(0, r.EarlyTotal);
    }

    [Fact]
    public void UnreturnedExit_UnusedPermissionIsExcused_RestIsUnexcused()
    {
        // Left at 11:00 with permission until 12:00, never came back; day closed at 11:00.
        var exit = new ExitSpan(T(11, 0), null, Span(11, 0, 12, 0));
        var r = Calc(T(8, 0), T(11, 0), CheckOutType.UnreturnedExit, exits: [exit]);
        Assert.Equal(300, r.EarlyTotal);
        Assert.Equal(60, r.EarlyExcused);
        Assert.Equal(240, r.EarlyUnexcused);
        Assert.Equal(0, r.PermissionMinutes);
        Assert.Equal(180, r.NetWorkMinutes);
    }

    [Fact]
    public void ShiftCrossingMidnight_LateAndOvertimeAreCorrect()
    {
        var night = ShiftTiming.Window(Day0, new TimeOnly(22, 0), new TimeOnly(6, 0));
        var r = Calc(T(22, 20), T(6, 30, dayOffset: 1), shift: night);
        Assert.Equal(20, r.LateTotal);
        Assert.Equal(30, r.OvertimeMinutes);
        Assert.Equal(490, r.GrossMinutes);
    }

    [Fact]
    public void CheckoutBeforeCheckin_IsRejected() =>
        Assert.Throws<DomainException>(() => Calc(T(9, 0), T(8, 0)));
}
