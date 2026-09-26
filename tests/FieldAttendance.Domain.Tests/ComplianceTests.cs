using FieldAttendance.Domain.Attendance;
using FieldAttendance.Domain.Enums;
using Xunit;

namespace FieldAttendance.Domain.Tests;

public class ComplianceTests
{
    private const int Day = 450; // 08:00–16:00 less a 30-minute break

    private static ComplianceDay Worked(int lateUnexcused = 0, int earlyUnexcused = 0) =>
        new(AttendanceStatus.CheckedOut, Day, lateUnexcused, earlyUnexcused);

    [Fact]
    public void Fifteen_days_on_time_is_one_hundred_percent()
    {
        var r = ComplianceCalculator.Calculate(Enumerable.Repeat(Worked(), 15));
        Assert.Equal(100m, r.Percent);
        Assert.Equal(15, r.CountedDays);
    }

    [Fact]
    public void A_short_day_lowers_the_figure_in_proportion_to_the_minutes_missed()
    {
        var days = Enumerable.Repeat(Worked(), 15).Append(Worked(lateUnexcused: 45, earlyUnexcused: 15));

        // (15 × 450 + 390) / (16 × 450) = 7140 / 7200
        Assert.Equal(99.2m, ComplianceCalculator.Calculate(days).Percent);
    }

    [Fact]
    public void An_absence_counts_as_a_day_with_nothing_honoured()
    {
        var r = ComplianceCalculator.Calculate([Worked(), new ComplianceDay(AttendanceStatus.Absent, Day, 0, 0)]);
        Assert.Equal(50m, r.Percent);
    }

    [Fact]
    public void Leave_days_and_days_not_yet_over_are_left_out_entirely()
    {
        var r = ComplianceCalculator.Calculate([
            Worked(),
            new ComplianceDay(AttendanceStatus.OnLeave, Day, 0, 0),
            new ComplianceDay(AttendanceStatus.Present, Day, 30, 0),
            new ComplianceDay(AttendanceStatus.Scheduled, Day, 0, 0),
        ]);
        Assert.Equal(100m, r.Percent);
        Assert.Equal(1, r.CountedDays);
    }

    [Fact]
    public void A_day_can_never_count_below_zero()
    {
        var r = ComplianceCalculator.Calculate([Worked(), Worked(lateUnexcused: 400, earlyUnexcused: 200)]);
        Assert.Equal(50m, r.Percent);
    }

    [Fact]
    public void No_settled_day_yet_gives_no_figure_rather_than_zero_or_full_marks() =>
        Assert.Null(ComplianceCalculator.Calculate([new ComplianceDay(AttendanceStatus.Scheduled, Day, 0, 0)]).Percent);
}
