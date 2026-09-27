namespace FieldAttendance.Domain.Payroll;

/// <summary>One recorded salary change, reduced to what the timeline needs.</summary>
public readonly record struct SalaryStep(DateOnly EffectiveFrom, decimal OldSalary, decimal NewSalary, DateTimeOffset RecordedAt);

/// <summary>
/// Which basic salary applies on a day or across a month. A change counts from its effective date,
/// not from the day it was entered, so a raise dated next month is not paid this month and a
/// back-dated correction is paid from the date it names.
/// </summary>
public static class SalaryTimeline
{
    /// <param name="fallback">Used only when there is no recorded change at all (data older than salary history).</param>
    public static decimal On(IReadOnlyCollection<SalaryStep> steps, DateOnly day, decimal fallback)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0) return fallback;

        var applying = steps.Where(s => s.EffectiveFrom <= day)
            .OrderBy(s => s.EffectiveFrom).ThenBy(s => s.RecordedAt).LastOrDefault();
        if (applying != default) return applying.NewSalary;

        // Before the first change: the figure that change replaced (0 before hire).
        return steps.OrderBy(s => s.EffectiveFrom).ThenBy(s => s.RecordedAt).First().OldSalary;
    }

    /// <summary>
    /// The basic salary for a pay period: each salary weighted by the calendar days it covers,
    /// so an unchanged salary is paid in full whatever the month's length.
    /// </summary>
    public static decimal ForPeriod(IReadOnlyCollection<SalaryStep> steps, DateOnly first, DateOnly last, decimal fallback)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentOutOfRangeException.ThrowIfLessThan(last, first);

        var boundaries = steps.Select(s => s.EffectiveFrom).Where(d => d > first && d <= last)
            .Append(first).Distinct().Order().ToList();
        var totalDays = last.DayNumber - first.DayNumber + 1;

        var weighted = 0m;
        for (var i = 0; i < boundaries.Count; i++)
        {
            var end = i + 1 < boundaries.Count ? boundaries[i + 1].AddDays(-1) : last;
            var days = end.DayNumber - boundaries[i].DayNumber + 1;
            weighted += On(steps, boundaries[i], fallback) * days;
        }
        return Math.Round(weighted / totalDays, 2, MidpointRounding.AwayFromZero);
    }
}
