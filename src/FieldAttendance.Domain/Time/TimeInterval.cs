using FieldAttendance.Domain.Common;

namespace FieldAttendance.Domain.Time;

/// <summary>Half-open interval [Start, End) in UAE time, minute precision.</summary>
public readonly record struct TimeInterval
{
    public TimeInterval(DateTimeOffset start, DateTimeOffset end)
    {
        var s = UaeTime.TruncateToMinute(start);
        var e = UaeTime.TruncateToMinute(end);
        if (e < s)
            throw new DomainException("interval.end_before_start", "Interval end must not be before its start.");
        Start = s;
        End = e;
    }

    public DateTimeOffset Start { get; }
    public DateTimeOffset End { get; }

    public int Minutes => (int)(End - Start).TotalMinutes;

    public bool Contains(DateTimeOffset instant) => instant >= Start && instant < End;

    public bool Overlaps(TimeInterval other) => Start < other.End && other.Start < End;

    public TimeInterval? Intersect(TimeInterval other)
    {
        var start = Start > other.Start ? Start : other.Start;
        var end = End < other.End ? End : other.End;
        return end > start ? new TimeInterval(start, end) : null;
    }

    /// <summary>
    /// Minutes of this interval covered by any of <paramref name="windows"/>.
    /// Windows are merged first so overlapping permissions are never counted twice.
    /// </summary>
    public int CoveredMinutes(IEnumerable<TimeInterval> windows)
    {
        var total = 0;
        foreach (var window in Merge(windows))
        {
            if (Intersect(window) is { } overlap)
                total += overlap.Minutes;
        }
        return total;
    }

    public static IReadOnlyList<TimeInterval> Merge(IEnumerable<TimeInterval> intervals)
    {
        var ordered = intervals.Where(i => i.Minutes > 0).OrderBy(i => i.Start).ToList();
        var merged = new List<TimeInterval>(ordered.Count);
        foreach (var current in ordered)
        {
            if (merged.Count > 0 && current.Start <= merged[^1].End)
            {
                var last = merged[^1];
                merged[^1] = new TimeInterval(last.Start, current.End > last.End ? current.End : last.End);
            }
            else
            {
                merged.Add(current);
            }
        }
        return merged;
    }
}
