using Tamaneen.SharedKernel;

namespace Tamaneen.Crisis;

/// <summary>
/// While a lock is active the patient's chat is closed: every new message gets the crisis screen and is never
/// sent to the AI. Only a clinician closing the crisis event releases it.
/// </summary>
public sealed class ChatLock : Entity
{
    private ChatLock(Guid id, Guid patientId, Guid crisisEventId, DateTimeOffset lockedAtUtc)
        : base(id)
    {
        PatientId = patientId;
        CrisisEventId = crisisEventId;
        LockedAtUtc = lockedAtUtc;
    }

    // EF Core
    private ChatLock()
    {
    }

    public Guid PatientId { get; private set; }

    public Guid CrisisEventId { get; private set; }

    public DateTimeOffset LockedAtUtc { get; private set; }

    public DateTimeOffset? ReleasedAtUtc { get; private set; }

    public Guid? ReleasedByUserId { get; private set; }

    public bool IsActive => ReleasedAtUtc is null;

    public static ChatLock For(CrisisEvent crisisEvent, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(crisisEvent);
        return new ChatLock(Guid.NewGuid(), crisisEvent.PatientId, crisisEvent.Id, clock.UtcNow);
    }

    public void Release(Guid clinicianUserId, IClock clock)
    {
        if (!IsActive)
        {
            return;
        }

        ReleasedByUserId = clinicianUserId;
        ReleasedAtUtc = clock.UtcNow;
    }
}
