using FieldAttendance.Domain.Attendance;
using FieldAttendance.Domain;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Time;
using Xunit;

namespace FieldAttendance.Domain.Tests;

/// <summary>
/// A night shift is where naive time arithmetic breaks: the end time is "smaller" than the
/// start, so subtracting clock times gives a negative day. These fix the behaviour.
/// </summary>
public class OvernightShiftTests
{
    private static readonly DateOnly Day = new(2026, 9, 24);

    // 22:00 → 06:00 the next morning.
    private static TimeInterval Night() => ShiftTiming.Window(Day, new TimeOnly(22, 0), new TimeOnly(6, 0));

    private static DateTimeOffset At(int day, int hour, int minute) =>
        OrgTime.At(Day.AddDays(day), new TimeOnly(hour, minute));

    private static AttendanceCalculationResult Calc(DateTimeOffset checkIn, DateTimeOffset checkOut, int grace = 10) =>
        AttendanceCalculator.Calculate(new AttendanceCalculationInput(
            ScheduledWindow: Night(), BreakMinutes: 0, GraceMinutes: grace,
            CountEarlyArrivalAsOvertime: false, MinimumOvertimeMinutes: 0,
            CheckInAt: checkIn, CheckOutAt: checkOut, CheckOutType: Enums.CheckOutType.Normal,
            Exits: [], ApprovedLateWindows: [], ApprovedEarlyDepartureWindows: []));

    [Fact]
    public void TheWindow_EndsOnTheFollowingDay()
    {
        var window = Night();
        Assert.Equal(Day, OrgTime.DateOf(window.Start));
        Assert.Equal(Day.AddDays(1), OrgTime.DateOf(window.End));
        Assert.Equal(480, (int)(window.End - window.Start).TotalMinutes);
    }

    [Fact]
    public void AFullNight_CountsAsEightHours() =>
        Assert.Equal(480, Calc(At(0, 22, 0), At(1, 6, 0)).NetWorkMinutes);

    [Fact]
    public void ArrivingWithinGrace_IsNotLate() =>
        Assert.Equal(0, Calc(At(0, 22, 8), At(1, 6, 0)).LateUnexcused);

    [Fact]
    public void PastGrace_LatenessCountsFromTheShiftStart_NotFromTheGraceEnd()
    {
        var result = Calc(At(0, 22, 25), At(1, 6, 0));
        Assert.Equal(25, result.LateUnexcused);
    }

    [Fact]
    public void LeavingBeforeTheMorning_IsEarlyDeparture()
    {
        var result = Calc(At(0, 22, 0), At(1, 5, 30));
        Assert.Equal(30, result.EarlyUnexcused);
        Assert.Equal(450, result.NetWorkMinutes);
    }

    [Fact]
    public void StayingPastTheEnd_IsOvertime_AcrossMidnight()
    {
        var result = Calc(At(0, 22, 0), At(1, 7, 0));
        Assert.Equal(60, result.OvertimeMinutes);
    }

    [Fact]
    public void ArrivingEarlyAtNight_DoesNotInflateTheHours()
    {
        var result = Calc(At(0, 21, 30), At(1, 6, 0));
        Assert.Equal(480, result.NetWorkMinutes);
        Assert.Equal(0, result.OvertimeMinutes);
    }

    [Fact]
    public void APermissionTime_MapsOntoTheCorrectSideOfMidnight()
    {
        // 02:00 belongs to the morning after the shift started.
        var mapped = ShiftTiming.MapIntoWindow(Night(), new TimeOnly(2, 0));
        Assert.NotNull(mapped);
        Assert.Equal(Day.AddDays(1), OrgTime.DateOf(mapped!.Value));
    }

    [Fact]
    public void ATimeOutsideTheShift_IsRejected() =>
        Assert.Null(ShiftTiming.MapIntoWindow(Night(), new TimeOnly(12, 0)));

    [Fact]
    public void GraceIsToleranceOnly_SoOnTimeStaysZero()
    {
        var onTime = Calc(At(0, 22, 0), At(1, 6, 0));
        Assert.Equal(0, onTime.LateTotal);
        Assert.Equal(0, onTime.LateUnexcused);
    }
}
