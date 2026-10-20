using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Scheduling;

public enum SlotStatus
{
    Open = 0,
    Held = 1,
    Booked = 2,
}

/// <summary>
/// A bookable time of a therapist. A patient holds it for five minutes while confirming (P08). An expired
/// hold counts as open again, so no cleanup job is needed for correctness. RowVersion makes two patients
/// racing for the same slot resolve to one winner.
/// </summary>
public sealed class AvailabilitySlot : Entity
{
    public static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(5);

    private AvailabilitySlot(Guid id, Guid therapistUserId, DateTimeOffset startsAtUtc, int durationMinutes)
        : base(id)
    {
        TherapistUserId = therapistUserId;
        StartsAtUtc = startsAtUtc;
        DurationMinutes = durationMinutes;
    }

    // EF Core
    private AvailabilitySlot()
    {
    }

    public Guid TherapistUserId { get; private set; }

    public DateTimeOffset StartsAtUtc { get; private set; }

    public int DurationMinutes { get; private set; }

    public SlotStatus Status { get; private set; }

    public Guid? HoldId { get; private set; }

    public Guid? HeldByPatientId { get; private set; }

    public DateTimeOffset? HeldUntilUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public static AvailabilitySlot Create(Guid therapistUserId, DateTimeOffset startsAtUtc, int durationMinutes) =>
        durationMinutes is < 15 or > 180
            ? throw new ArgumentOutOfRangeException(nameof(durationMinutes))
            : new AvailabilitySlot(Guid.NewGuid(), therapistUserId, startsAtUtc, durationMinutes);

    public bool IsAvailable(IClock clock) =>
        StartsAtUtc > clock.UtcNow && (Status == SlotStatus.Open || (Status == SlotStatus.Held && HeldUntilUtc <= clock.UtcNow));

    public Guid Hold(Guid patientId, IClock clock)
    {
        if (!IsAvailable(clock))
        {
            throw new DomainException(ErrorCodes.SlotUnavailable, $"Slot {Id} is not available.");
        }

        Status = SlotStatus.Held;
        HoldId = Guid.NewGuid();
        HeldByPatientId = patientId;
        HeldUntilUtc = clock.UtcNow + HoldDuration;
        return HoldId.Value;
    }

    public bool IsHeldBy(Guid holdId, Guid patientId, IClock clock) =>
        Status == SlotStatus.Held && HoldId == holdId && HeldByPatientId == patientId && HeldUntilUtc > clock.UtcNow;

    public void Release()
    {
        if (Status == SlotStatus.Held)
        {
            Status = SlotStatus.Open;
            HoldId = null;
            HeldByPatientId = null;
            HeldUntilUtc = null;
        }
    }

    public void Book(Guid holdId, Guid patientId, IClock clock)
    {
        if (!IsHeldBy(holdId, patientId, clock))
        {
            throw new DomainException(ErrorCodes.HoldExpired, $"Hold {holdId} on slot {Id} expired or belongs to someone else.");
        }

        Status = SlotStatus.Booked;
        HeldUntilUtc = null;
    }

    /// <summary>Opens a booked slot again after a cancellation.</summary>
    public void Free()
    {
        Status = SlotStatus.Open;
        HoldId = null;
        HeldByPatientId = null;
        HeldUntilUtc = null;
    }
}
