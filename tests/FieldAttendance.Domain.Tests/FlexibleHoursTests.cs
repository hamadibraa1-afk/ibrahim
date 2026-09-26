using FieldAttendance.Domain.Attendance;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Geo;
using FieldAttendance.Domain.Time;
using Xunit;
using static FieldAttendance.Domain.Tests.TestTime;

namespace FieldAttendance.Domain.Tests;

/// <summary>Standard shift 08:00–16:00, grace 10, with 30 minutes of flexible hours.</summary>
public class FlexibleHoursTests
{
    private static AttendanceCalculationResult Calc(DateTimeOffset checkIn, DateTimeOffset checkOut, int flex = 30,
        bool countEarly = false, TimeInterval[]? late = null) =>
        AttendanceCalculator.Calculate(new AttendanceCalculationInput(
            StandardShift, 0, 10, countEarly, 0, checkIn, checkOut, CheckOutType.Normal, [], late ?? [], [], flex));

    [Fact]
    public void Arriving_two_minutes_early_moves_the_whole_day_two_minutes_earlier()
    {
        Assert.Equal(Span(7, 58, 15, 58), FlexibleHours.EffectiveWindow(StandardShift, T(7, 58), 30));

        var r = Calc(T(7, 58), T(15, 58));
        Assert.Equal(0, r.LateTotal);
        Assert.Equal(0, r.EarlyTotal);
        Assert.Equal(480, r.NetWorkMinutes);
    }

    [Fact]
    public void Arriving_inside_the_flex_window_is_on_time_and_the_day_ends_later()
    {
        var r = Calc(T(8, 20), T(16, 20));
        Assert.Equal(0, r.LateTotal);
        Assert.Equal(0, r.EarlyTotal);
        Assert.Equal(480, r.NetWorkMinutes);
    }

    [Fact]
    public void Leaving_at_the_scheduled_end_after_a_late_flex_start_is_early_departure() =>
        Assert.Equal(20, Calc(T(8, 20), T(16, 0)).EarlyUnexcused);

    [Fact]
    public void Past_the_flex_window_lateness_counts_from_the_end_of_flex_not_the_scheduled_start()
    {
        var r = Calc(T(8, 40), T(16, 30));
        Assert.Equal(10, r.LateTotal);
        Assert.Equal(10, r.LateUnexcused);
        Assert.Equal(0, r.EarlyTotal);
    }

    [Fact]
    public void Flex_replaces_the_grace_period_instead_of_adding_to_it() =>
        Assert.Equal(5, Calc(T(8, 35), T(16, 30)).LateTotal);

    [Fact]
    public void Arriving_before_the_flex_window_starts_the_day_at_the_earliest_flex_time()
    {
        Assert.Equal(Span(7, 30, 15, 30), FlexibleHours.EffectiveWindow(StandardShift, T(7, 10), 30));

        var r = Calc(T(7, 10), T(15, 30));
        Assert.Equal(0, r.EarlyTotal);
        Assert.Equal(480, r.NetWorkMinutes);
        Assert.Equal(0, r.OvertimeMinutes);
    }

    [Fact]
    public void Overtime_starts_after_the_personal_end() =>
        Assert.Equal(25, Calc(T(7, 45), T(16, 10)).OvertimeMinutes);

    [Fact]
    public void An_approved_late_permission_still_excuses_lateness_past_the_flex_window()
    {
        var r = Calc(T(8, 50), T(16, 30), late: [Span(8, 0, 9, 0)]);
        Assert.Equal(20, r.LateTotal);
        Assert.Equal(20, r.LateExcused);
        Assert.Equal(0, r.LateUnexcused);
    }

    [Fact]
    public void Without_flex_the_scheduled_window_is_unchanged() =>
        Assert.Equal(StandardShift, FlexibleHours.EffectiveWindow(StandardShift, T(7, 58), 0));

    [Fact]
    public void Check_in_opens_at_the_start_of_the_flex_window_even_without_an_early_check_in_allowance()
    {
        var template = Builders.Morning();
        template.SetRules(10, false, earlyCheckInMinutes: 0);
        template.SetFlex(30);
        var record = Builders.Record(template);

        record.CheckIn(T(7, 30), new GeoPoint(25.3, 55.4), 10, 5, CheckInType.Normal);

        Assert.Equal(T(15, 30), record.EffectiveWindow.End);
    }

    [Fact]
    public void A_forgotten_check_out_closes_at_the_personal_end_not_the_scheduled_one()
    {
        var template = Builders.Morning();
        template.SetFlex(30);
        var record = Builders.Record(template);
        record.CheckIn(T(8, 30), new GeoPoint(25.3, 55.4), 10, 5, CheckInType.Normal);

        Assert.False(record.AutoClose(T(17, 0), delayMinutes: 60));
        Assert.True(record.AutoClose(T(17, 30), delayMinutes: 60));
        Assert.Equal(T(16, 30), record.CheckOutAt);
    }

    [Fact]
    public void Editing_the_rules_from_the_shifts_screen_keeps_flexible_hours()
    {
        var template = Builders.Morning();
        template.SetFlex(30);

        template.SetRules(5, true, 15);

        Assert.Equal(30, template.FlexMinutes);
    }
}
