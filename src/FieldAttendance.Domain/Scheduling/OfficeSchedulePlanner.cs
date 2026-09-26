using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;

namespace FieldAttendance.Domain.Scheduling;

/// <param name="Days">Week days that share these times.</param>
public sealed record OfficeShiftPlan(TimeOnly Start, TimeOnly End, int BreakMinutes, WorkDays Days);

/// <summary>
/// Turns a fixed weekly office pattern into the same assignment shape the field module uses.
/// Office attendance therefore runs through one proven engine — resolver, materializer,
/// calculator — instead of a second implementation that could drift from it.
///
/// Days with identical times are grouped into one assignment, so a Sunday-to-Thursday
/// pattern produces one row, and a shorter Friday produces a second.
/// </summary>
public static class OfficeSchedulePlanner
{
    public static IReadOnlyList<OfficeShiftPlan> Plan(WorkSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        schedule.EnsureUsable();

        var groups = schedule.TimeGroups()
            .Select(g => new OfficeShiftPlan(g.Start, g.End, g.BreakMinutes, g.Days))
            .ToList();

        EnsureNoOverlap(schedule, groups);
        return groups;
    }

    /// <summary>
    /// The assignment layer rejects an employee being in two places at once, so a pattern
    /// that overlaps itself must be caught here with a message that points at the schedule.
    /// </summary>
    private static void EnsureNoOverlap(WorkSchedule schedule, IReadOnlyList<OfficeShiftPlan> groups)
    {
        if (groups.Count < 2) return;

        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var onDay = groups.Where(g => g.Days.Includes(day)).OrderBy(g => g.Start).ToList();
            for (var i = 1; i < onDay.Count; i++)
            {
                var previousEnd = onDay[i - 1].End < onDay[i - 1].Start ? TimeOnly.MaxValue : onDay[i - 1].End;
                if (onDay[i].Start < previousEnd)
                    throw new DomainException("schedule.overlapping_days",
                        $"{schedule.NameAr}: working times overlap on the same day.");
            }
        }
    }
}
