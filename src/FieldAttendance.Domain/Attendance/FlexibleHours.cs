using FieldAttendance.Domain.Time;

namespace FieldAttendance.Domain.Attendance;

/// <summary>
/// Flexible hours: the working day keeps its length but moves with the arrival, up to
/// <c>flexMinutes</c> either side of the scheduled start. Arriving at 07:58 for an 08:00–15:00 day
/// makes the day 07:58–14:58; arriving at 08:40 with 30 minutes of flex makes it 08:30–15:30 and
/// the ten minutes past 08:30 are lateness.
///
/// Every rule that reads the shift boundaries (lateness, early departure, overtime, auto-close)
/// reads this window instead, so flexible and fixed days go through the same calculator.
/// </summary>
public static class FlexibleHours
{
    public static TimeInterval EffectiveWindow(TimeInterval scheduled, DateTimeOffset? checkIn, int flexMinutes)
    {
        ArgumentNullException.ThrowIfNull(scheduled);
        if (flexMinutes <= 0 || checkIn is not { } at)
            return scheduled;

        var offset = Math.Clamp((int)(UaeTime.TruncateToMinute(at) - scheduled.Start).TotalMinutes, -flexMinutes, flexMinutes);
        return new TimeInterval(scheduled.Start.AddMinutes(offset), scheduled.End.AddMinutes(offset));
    }

    /// <summary>
    /// With flexible hours the flex window replaces the grace period: it is already a wider
    /// tolerance, and stacking the two would make 08:40 on-time under a 30-minute flex.
    /// </summary>
    public static int EffectiveGrace(int graceMinutes, int flexMinutes) => flexMinutes > 0 ? 0 : graceMinutes;
}
