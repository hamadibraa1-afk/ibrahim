using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Scheduling;

public enum AppointmentStatus
{
    Confirmed = 0,
    CancelledByPatient = 1,
    CancelledByTherapist = 2,
    Completed = 3,
    NoShow = 4,
}

/// <summary>A booked remote session. Patients may change or cancel it until 24 hours before.</summary>
public sealed class Appointment : Entity
{
    public static readonly TimeSpan ChangeCutoff = TimeSpan.FromHours(24);
    public static readonly TimeSpan JoinOpensBefore = TimeSpan.FromMinutes(10);

    private Appointment(Guid id, Guid patientId, Guid therapistUserId, Guid slotId, DateTimeOffset startsAtUtc, int durationMinutes, int sequenceNumber, string roomId, DateTimeOffset now)
        : base(id)
    {
        PatientId = patientId;
        TherapistUserId = therapistUserId;
        SlotId = slotId;
        StartsAtUtc = startsAtUtc;
        DurationMinutes = durationMinutes;
        SequenceNumber = sequenceNumber;
        RoomId = roomId;
        CreatedAtUtc = now;
    }

    // EF Core
    private Appointment()
    {
        RoomId = string.Empty;
    }

    public Guid PatientId { get; private set; }

    public Guid TherapistUserId { get; private set; }

    public Guid SlotId { get; private set; }

    public DateTimeOffset StartsAtUtc { get; private set; }

    public int DurationMinutes { get; private set; }

    /// <summary>1 for the first session with this therapist, 2 for the second…</summary>
    public int SequenceNumber { get; private set; }

    public AppointmentStatus Status { get; private set; }

    public bool Rescheduled { get; private set; }

    /// <summary>Unguessable id of the video room; the join link is built from it only shortly before the session.</summary>
    public string RoomId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    /// <summary>Code from <see cref="CancelReasons"/> chosen by the patient when they cancelled (PB-45); null otherwise.</summary>
    public string? CancelReason { get; private set; }

    public DateTimeOffset? OutcomeRecordedAtUtc { get; private set; }

    public static Appointment Book(Guid patientId, AvailabilitySlot slot, int sequenceNumber, string roomId, IClock clock) =>
        new(Guid.NewGuid(), patientId, slot.TherapistUserId, slot.Id, slot.StartsAtUtc, slot.DurationMinutes, sequenceNumber, roomId, clock.UtcNow);

    public DateTimeOffset ChangeDeadlineUtc => StartsAtUtc - ChangeCutoff;

    public bool CanChange(IClock clock) => Status == AppointmentStatus.Confirmed && clock.UtcNow < ChangeDeadlineUtc;

    public bool JoinOpen(IClock clock) =>
        Status == AppointmentStatus.Confirmed && clock.UtcNow >= StartsAtUtc - JoinOpensBefore && clock.UtcNow <= StartsAtUtc.AddMinutes(DurationMinutes);

    public void CancelByPatient(string reasonCode, IClock clock)
    {
        if (!CancelReasons.Patient.Contains(reasonCode))
        {
            throw new ArgumentOutOfRangeException(nameof(reasonCode));
        }

        EnsureChangeable(clock);
        Status = AppointmentStatus.CancelledByPatient;
        CancelledAtUtc = clock.UtcNow;
        CancelReason = reasonCode;
    }

    public void CancelByTherapist(IClock clock)
    {
        if (Status != AppointmentStatus.Confirmed)
        {
            throw new DomainException(ErrorCodes.AppointmentNotChangeable, $"Appointment {Id} is not confirmed.");
        }

        Status = AppointmentStatus.CancelledByTherapist;
        CancelledAtUtc = clock.UtcNow;
    }

    public void MoveTo(AvailabilitySlot slot, IClock clock)
    {
        EnsureChangeable(clock);
        if (slot.TherapistUserId != TherapistUserId)
        {
            throw new DomainException(ErrorCodes.SlotUnavailable, "A session can only move to the same therapist's slot.");
        }

        SlotId = slot.Id;
        StartsAtUtc = slot.StartsAtUtc;
        DurationMinutes = slot.DurationMinutes;
        Rescheduled = true;
    }

    /// <summary>
    /// P54: the therapist has not joined after the start time. Moving the session is then free and not bound by the
    /// 24-hour limit; the caller has checked that the therapist is late.
    /// </summary>
    public void MoveAfterTherapistLate(AvailabilitySlot slot, IClock clock)
    {
        if (Status != AppointmentStatus.Confirmed || clock.UtcNow < StartsAtUtc)
        {
            throw new DomainException(ErrorCodes.AppointmentNotChangeable, $"Appointment {Id} is not waiting for a late therapist.");
        }

        if (slot.TherapistUserId != TherapistUserId)
        {
            throw new DomainException(ErrorCodes.SlotUnavailable, "A session can only move to the same therapist's slot.");
        }

        SlotId = slot.Id;
        StartsAtUtc = slot.StartsAtUtc;
        DurationMinutes = slot.DurationMinutes;
        Rescheduled = true;
    }

    public void RecordOutcome(AppointmentStatus outcome, IClock clock)
    {
        if (outcome is not (AppointmentStatus.Completed or AppointmentStatus.NoShow or AppointmentStatus.CancelledByTherapist))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        if (clock.UtcNow < StartsAtUtc - JoinOpensBefore && outcome != AppointmentStatus.CancelledByTherapist)
        {
            throw new DomainException(ErrorCodes.AppointmentNotStarted, $"Appointment {Id} has not started yet.");
        }

        Status = outcome;
        OutcomeRecordedAtUtc = clock.UtcNow;
        if (outcome == AppointmentStatus.CancelledByTherapist)
        {
            CancelledAtUtc = clock.UtcNow;
        }
    }

    private void EnsureChangeable(IClock clock)
    {
        if (!CanChange(clock))
        {
            throw new DomainException(ErrorCodes.AppointmentNotChangeable, $"Appointment {Id} can no longer be changed.");
        }
    }
}
