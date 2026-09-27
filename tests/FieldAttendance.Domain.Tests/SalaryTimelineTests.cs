using FieldAttendance.Domain.Payroll;
using Xunit;

namespace FieldAttendance.Domain.Tests;

/// <summary>A salary change counts from its effective date, not from the day it was typed in.</summary>
public class SalaryTimelineTests
{
    private static readonly DateTimeOffset Recorded = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly SalaryStep[] Hired5000RaisedTo6000OnOct1 =
    [
        new(new DateOnly(2025, 1, 1), 0m, 5000m, Recorded),
        new(new DateOnly(2026, 10, 1), 5000m, 6000m, Recorded.AddMonths(8)),
    ];

    [Fact]
    public void A_raise_dated_next_month_does_not_change_this_month()
    {
        Assert.Equal(5000m, SalaryTimeline.On(Hired5000RaisedTo6000OnOct1, new DateOnly(2026, 9, 30), 6000m));
        Assert.Equal(5000m, SalaryTimeline.ForPeriod(Hired5000RaisedTo6000OnOct1, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), 6000m));
        Assert.Equal(6000m, SalaryTimeline.ForPeriod(Hired5000RaisedTo6000OnOct1, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 6000m));
    }

    [Fact]
    public void A_raise_in_the_middle_of_the_month_is_paid_for_the_days_it_covers()
    {
        SalaryStep[] steps = [new(new DateOnly(2025, 1, 1), 0m, 5000m, Recorded), new(new DateOnly(2026, 9, 15), 5000m, 6000m, Recorded)];

        // 14 days at 5,000 and 16 days at 6,000, out of September's 30.
        Assert.Equal(5533.33m, SalaryTimeline.ForPeriod(steps, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), 6000m));
    }

    [Fact]
    public void Two_changes_on_the_same_date_the_later_entry_wins()
    {
        SalaryStep[] steps = [new(new DateOnly(2026, 9, 1), 5000m, 5500m, Recorded), new(new DateOnly(2026, 9, 1), 5000m, 5200m, Recorded.AddDays(1))];
        Assert.Equal(5200m, SalaryTimeline.On(steps, new DateOnly(2026, 9, 10), 0m));
    }

    [Fact]
    public void Before_the_first_change_the_salary_is_what_that_change_replaced()
    {
        Assert.Equal(0m, SalaryTimeline.On(Hired5000RaisedTo6000OnOct1, new DateOnly(2024, 12, 31), 6000m));
        Assert.Equal(4000m, SalaryTimeline.On([], new DateOnly(2026, 9, 1), 4000m));
    }
}
