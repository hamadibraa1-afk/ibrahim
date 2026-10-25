using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Sessions;

public enum SessionParticipant
{
    Patient = 0,
    Therapist = 1,
}

/// <summary>
/// Who is in the session room (P41, P42 and the therapist's session page). Each side's app sends a heartbeat while
/// the room is open; the other side sees "has joined" while the heartbeats keep coming. No audio or video passes
/// through here, only these timestamps.
/// </summary>
public sealed class SessionPresence : Entity
{
    /// <summary>Without a heartbeat for this long the participant counts as gone (the apps beat every 15 seconds).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(45);

    private SessionPresence(Guid id, Guid appointmentId, Guid patientId, Guid therapistUserId, SessionParticipant participant, DateTimeOffset now)
        : base(id)
    {
        AppointmentId = appointmentId;
        PatientId = patientId;
        TherapistUserId = therapistUserId;
        Participant = participant;
        FirstJoinedAtUtc = now;
        LastSeenAtUtc = now;
    }

    // EF Core
    private SessionPresence()
    {
    }

    public Guid AppointmentId { get; private set; }

    public Guid PatientId { get; private set; }

    public Guid TherapistUserId { get; private set; }

    public SessionParticipant Participant { get; private set; }

    public DateTimeOffset FirstJoinedAtUtc { get; private set; }

    public DateTimeOffset LastSeenAtUtc { get; private set; }

    public DateTimeOffset? LeftAtUtc { get; private set; }

    public static SessionPresence Join(Guid appointmentId, Guid patientId, Guid therapistUserId, SessionParticipant participant, IClock clock) =>
        new(Guid.NewGuid(), appointmentId, patientId, therapistUserId, participant, clock.UtcNow);

    public void Beat(IClock clock)
    {
        LastSeenAtUtc = clock.UtcNow;
        LeftAtUtc = null;
    }

    public void Leave(IClock clock)
    {
        LastSeenAtUtc = clock.UtcNow;
        LeftAtUtc = clock.UtcNow;
    }

    public bool IsPresent(DateTimeOffset now) => LeftAtUtc is null && now - LastSeenAtUtc <= Timeout;
}
