using FieldAttendance.Domain.Time;

namespace FieldAttendance.Domain.Scheduling;

public sealed record ShiftConflict(ResolvedShift First, ResolvedShift Second);

public sealed record CapacityViolation(Guid LocationId, TimeInterval During, int Assigned, int Capacity);

/// <summary>
/// Hard constraints applied on every schedule save (autosave included).
/// Callers must resolve one day before the edited range too, so a shift that
/// crosses midnight is checked against the next morning.
/// </summary>
public static class ScheduleValidator
{
    public static IReadOnlyList<ShiftConflict> FindEmployeeOverlaps(IEnumerable<ResolvedShift> shifts)
    {
        var conflicts = new List<ShiftConflict>();
        foreach (var group in shifts.GroupBy(s => s.EmployeeId))
        {
            var ordered = group.OrderBy(s => s.Window.Start).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                for (var j = i + 1; j < ordered.Count && ordered[j].Window.Start < ordered[i].Window.End; j++)
                    conflicts.Add(new ShiftConflict(ordered[i], ordered[j]));
            }
        }
        return conflicts;
    }

    /// <summary>Sweep line over start/end events; reports each interval where concurrent shifts exceed capacity.</summary>
    public static IReadOnlyList<CapacityViolation> FindCapacityViolations(Guid locationId, int capacity, IEnumerable<ResolvedShift> shifts)
    {
        var events = shifts
            .Where(s => s.LocationId == locationId && !s.IsOnLeave)
            .SelectMany(s => new[] { (At: s.Window.Start, Delta: +1), (At: s.Window.End, Delta: -1) })
            .OrderBy(e => e.At)
            .ThenBy(e => e.Delta) // ends before starts at the same instant: back-to-back shifts are fine
            .ToList();

        var violations = new List<CapacityViolation>();
        var running = 0;
        DateTimeOffset? overSince = null;
        var peak = 0;

        foreach (var (at, delta) in events)
        {
            running += delta;
            if (running > capacity)
            {
                overSince ??= at;
                peak = Math.Max(peak, running);
            }
            else if (overSince is { } since)
            {
                if (at > since)
                    violations.Add(new CapacityViolation(locationId, new TimeInterval(since, at), peak, capacity));
                overSince = null;
                peak = 0;
            }
        }
        return violations;
    }
}
