using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Scheduling;

/// <summary>A bookable time the pattern asks for.</summary>
public readonly record struct PlannedTime(DateTimeOffset StartsAtUtc, int DurationMinutes)
{
    public DateTimeOffset EndsAtUtc => StartsAtUtc.AddMinutes(DurationMinutes);
}

/// <summary>What saving the pattern does to the calendar.</summary>
public sealed record AvailabilityPlan(
    IReadOnlyList<PlannedTime> ToAdd,
    IReadOnlyList<AvailabilitySlot> ToRemove,
    IReadOnlyList<AvailabilitySlot> Affected);

/// <summary>
/// Turns the S19 pattern into bookable times and reconciles them with the slots that exist (SB-28). Pure, so the
/// rules are unit-tested: times are generated for the next eight weeks, in the therapist's time zone, as many
/// sessions plus gaps as fit in each period, skipping leave and absences, never earlier than the minimum notice.
/// Booked and held times are never removed or moved; they are reported as affected when the new pattern no
/// longer covers them. One-off (manual) times stay until the therapist removes them.
/// </summary>
public static class AvailabilityPlanner
{
    public const int HorizonDays = 56;

    public static IReadOnlyList<PlannedTime> Generate(
        IReadOnlyList<WeeklyPeriod> weekly,
        IReadOnlyList<AvailabilityBreak> exceptions,
        int sessionMinutes,
        int bufferMinutes,
        TimeZoneInfo zone,
        DateTimeOffset now,
        TimeSpan minimumNotice)
    {
        var earliest = now + minimumNotice;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var result = new List<PlannedTime>();
        for (var d = 0; d < HorizonDays; d++)
        {
            var day = today.AddDays(d);
            foreach (var period in weekly.Where(p => p.Day == (int)day.DayOfWeek).OrderBy(p => p.StartTime))
            {
                var start = day.ToDateTime(period.StartTime, DateTimeKind.Unspecified);
                var end = day.ToDateTime(period.EndTime, DateTimeKind.Unspecified);
                for (var t = start; t.AddMinutes(sessionMinutes) <= end; t = t.AddMinutes(sessionMinutes + bufferMinutes))
                {
                    if (zone.IsInvalidTime(t))
                    {
                        continue;
                    }

                    var utc = new DateTimeOffset(t, zone.GetUtcOffset(t)).ToUniversalTime();
                    var planned = new PlannedTime(utc, sessionMinutes);
                    if (utc > earliest && !exceptions.Any(e => e.StartsAtUtc < planned.EndsAtUtc && utc < e.EndsAtUtc))
                    {
                        result.Add(planned);
                    }
                }
            }
        }

        return result;
    }

    /// <summary>The end of the window the pattern manages: midnight after the last of the 56 days.</summary>
    public static DateTimeOffset HorizonEnd(TimeZoneInfo zone, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var local = today.AddDays(HorizonDays).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <param name="existing">The therapist's slots that start after the minimum notice and before the horizon end.</param>
    public static AvailabilityPlan Reconcile(IReadOnlyList<AvailabilitySlot> existing, IReadOnlyList<PlannedTime> desired, IReadOnlyList<AvailabilityBreak> exceptions, IClock clock)
    {
        var wanted = desired.ToHashSet();
        var kept = new List<AvailabilitySlot>();
        var remove = new List<AvailabilitySlot>();
        var affected = new List<AvailabilitySlot>();
        foreach (var slot in existing)
        {
            var key = new PlannedTime(slot.StartsAtUtc, slot.DurationMinutes);
            if (slot.IsTaken(clock))
            {
                kept.Add(slot);
                var covered = slot.Source == SlotSource.Template ? wanted.Contains(key) : !exceptions.Any(e => slot.Overlaps(e.StartsAtUtc, e.EndsAtUtc));
                if (!covered)
                {
                    affected.Add(slot);
                }
            }
            else if (slot.Source == SlotSource.Manual)
            {
                kept.Add(slot);
            }
            else if (wanted.Contains(key))
            {
                // Open stays open; a time the therapist took out stays out.
                kept.Add(slot);
            }
            else
            {
                remove.Add(slot);
            }
        }

        var add = new List<PlannedTime>();
        foreach (var time in desired)
        {
            if (!kept.Any(s => s.Overlaps(time.StartsAtUtc, time.EndsAtUtc)))
            {
                add.Add(time);
            }
        }

        return new AvailabilityPlan(add, remove, affected);
    }
}
