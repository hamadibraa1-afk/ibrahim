using Tamaneen.Clinical.Scheduling;

namespace Tamaneen.Clinical.Sessions;

/// <summary>
/// When the session room of an appointment is open (P41, P42, PB-43). It opens ten minutes before the start and stays
/// open for the booked length. A therapist who joins late does not shorten the session (§10.2): the room then stays
/// open for the full length from the moment they joined.
/// </summary>
public static class SessionWindow
{
    public static DateTimeOffset OpensAt(Appointment a) => a.StartsAtUtc - Appointment.JoinOpensBefore;

    public static DateTimeOffset ClosesAt(Appointment a, DateTimeOffset? therapistJoinedAtUtc)
    {
        var booked = a.StartsAtUtc.AddMinutes(a.DurationMinutes);
        if (therapistJoinedAtUtc is { } joined && joined > a.StartsAtUtc)
        {
            var extended = joined.AddMinutes(a.DurationMinutes);
            return extended > booked ? extended : booked;
        }

        return booked;
    }

    public static bool IsOpen(Appointment a, DateTimeOffset now, DateTimeOffset? therapistJoinedAtUtc) =>
        a.Status == AppointmentStatus.Confirmed && now >= OpensAt(a) && now <= ClosesAt(a, therapistJoinedAtUtc);

    /// <summary>
    /// P54: the start time has passed and the therapist has not joined. The lobby then shows the late state and the
    /// patient may move the session for free, without the 24-hour limit.
    /// </summary>
    public static bool TherapistLate(Appointment a, DateTimeOffset now, DateTimeOffset? therapistJoinedAtUtc) =>
        a.Status == AppointmentStatus.Confirmed && therapistJoinedAtUtc is null && now > a.StartsAtUtc && now <= ClosesAt(a, null);

    /// <summary>Feedback (P43) is possible once the session has started and was not cancelled or missed.</summary>
    public static bool FeedbackAllowed(Appointment a, DateTimeOffset now) =>
        a.Status is AppointmentStatus.Confirmed or AppointmentStatus.Completed && now >= a.StartsAtUtc;
}
