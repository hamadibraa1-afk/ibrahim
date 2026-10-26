using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Scheduling;

public enum SlotStatus
{
    Open = 0,
    Held = 1,
    Booked = 2,

    /// <summary>A generated time the therapist took out (S19). Kept so the next save does not generate it again; never bookable.</summary>
    Blocked = 3,
}

/// <summary>Where a slot came from: the weekly pattern of S19, or a one-off time the therapist added.</summary>
public enum SlotSource
{
    Template = 0,
    Manual = 1,
}

/// <summary>
/// A bookable time of a therapist. A patient holds it for five minutes while confirming (P08). An expired
/// hold counts as open again, so no cleanup job is needed for correctness. RowVersion makes two patients
/// racing for the same slot resolve to one winner.
/// </summary>
public sealed class AvailabilitySlot : Entity
{
    public static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(5);

    private AvailabilitySlot(Guid id, Guid therapistUserId, DateTimeOffset startsAtUtc, int durationMinutes, SlotSource source)
        : base(id)
    {
        TherapistUserId = therapistUserId;
        StartsAtUtc = startsAtUtc;
        DurationMinutes = durationMinutes;
        Source = source;
    }

    // EF Core
    private AvailabilitySlot()
    {
    }

    public Guid TherapistUserId { get; private set; }

    public DateTimeOffset StartsAtUtc { get; private set; }

    public int DurationMinutes { get; private set; }

    public SlotStatus Status { get; private set; }

    public SlotSource Source { get; private set; }

    public DateTimeOffset EndsAtUtc => StartsAtUtc.AddMinutes(DurationMinutes);

    public Guid? HoldId { get; private set; }

    public Guid? HeldByPatientId { get; private set; }

    public DateTimeOffset? HeldUntilUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public static AvailabilitySlot Create(Guid therapistUserId, DateTimeOffset startsAtUtc, int durationMinutes, SlotSource source = SlotSource.Template) =>
        durationMinutes is < 15 or > 180
            ? throw new ArgumentOutOfRangeException(nameof(durationMinutes))
            : new AvailabilitySlot(Guid.NewGuid(), therapistUserId, startsAtUtc, durationMinutes, source);

    /// <summary>Booked, or held by a patient whose hold has not run out: the therapist can neither remove nor move it.</summary>
    public bool IsTaken(IClock clock) => Status == SlotStatus.Booked || (Status == SlotStatus.Held && HeldUntilUtc > clock.UtcNow);

    public bool Overlaps(DateTimeOffset startUtc, DateTimeOffset endUtc) => StartsAtUtc < endUtc && startUtc < EndsAtUtc;

    /// <summary>Takes a free generated time out of the calendar (S19).</summary>
    public void Block(IClock clock)
    {
        EnsureFree(clock);
        Status = SlotStatus.Blocked;
        HoldId = null;
        HeldByPatientId = null;
        HeldUntilUtc = null;
    }

    /// <summary>Puts a time the therapist took out back on offer.</summary>
    public void Reopen(int durationMinutes)
    {
        if (Status == SlotStatus.Blocked)
        {
            Status = SlotStatus.Open;
            DurationMinutes = durationMinutes;
        }
    }

    /// <summary>Moves a free time (S19). A moved time is the therapist's own choice from then on, not the pattern's.</summary>
    public void MoveTo(DateTimeOffset startsAtUtc, int durationMinutes, IClock clock)
    {
        EnsureFree(clock);
        if (durationMinutes is < 15 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(durationMinutes));
        }

        StartsAtUtc = startsAtUtc;
        DurationMinutes = durationMinutes;
        Source = SlotSource.Manual;
        Release();
    }

    /// <summary>A free generated time the new pattern wants again with another length: open, with that length.</summary>
    public void ResetTo(int durationMinutes, IClock clock)
    {
        EnsureFree(clock);
        Release();
        Status = SlotStatus.Open;
        DurationMinutes = durationMinutes;
        Source = SlotSource.Template;
    }

    private void EnsureFree(IClock clock)
    {
        if (IsTaken(clock))
        {
            throw new DomainException(ErrorCodes.SlotTaken, $"Slot {Id} is booked or held.");
        }
    }

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

    /// <summary>Books the slot for a patient who is moving a session: it must be open, or held by that patient.</summary>
    public void TakeFor(Guid patientId, IClock clock)
    {
        var heldByPatient = Status == SlotStatus.Held && HeldByPatientId == patientId && HeldUntilUtc > clock.UtcNow;
        if (!heldByPatient && !IsAvailable(clock))
        {
            throw new DomainException(ErrorCodes.SlotUnavailable, $"Slot {Id} is not available.");
        }

        Status = SlotStatus.Booked;
        HoldId = null;
        HeldByPatientId = patientId;
        HeldUntilUtc = null;
    }

    /// <summary>
    /// After the therapist cancels: the time is taken out of the calendar, because the therapist said they cannot make
    /// it. They can put it back on offer from S19 (Reopen) if that changes.
    /// </summary>
    public void CloseAfterTherapistCancel()
    {
        Status = SlotStatus.Blocked;
        HoldId = null;
        HeldByPatientId = null;
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
