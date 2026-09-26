namespace FieldAttendance.Domain.Time;

public static class ShiftTiming
{
    /// <summary>
    /// Concrete window for a shift that starts on <paramref name="shiftDate"/>.
    /// End at or before start means the shift crosses midnight and ends the next day.
    /// </summary>
    public static TimeInterval Window(DateOnly shiftDate, TimeOnly start, TimeOnly end)
    {
        var startAt = UaeTime.At(shiftDate, start);
        var endDate = end <= start ? shiftDate.AddDays(1) : shiftDate;
        return new TimeInterval(startAt, UaeTime.At(endDate, end));
    }

    /// <summary>
    /// Maps a wall-clock time (as typed in a permission form) onto the shift window.
    /// Handles shifts that cross midnight. Returns null if the time falls outside the shift.
    /// </summary>
    public static DateTimeOffset? MapIntoWindow(TimeInterval window, TimeOnly time)
    {
        var startDate = UaeTime.DateOf(window.Start);
        foreach (var candidate in new[] { UaeTime.At(startDate, time), UaeTime.At(startDate.AddDays(1), time) })
        {
            if (candidate >= window.Start && candidate <= window.End)
                return candidate;
        }
        return null;
    }
}
