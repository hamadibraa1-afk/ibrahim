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

    /// <summary>"Running late" from the therapist (S18): how many minutes, for the patient's lobby (P54).</summary>
    public int? TherapistLateMinutes { get; private set; }

    public DateTimeOffset? TherapistLateAtUtc { get; private set; }

    /// <summary>The therapist's optional note on a cancellation, encrypted with the patient's key; never shown to the patient.</summary>
    public byte[]? TherapistCancelNoteCipher { get; private set; }

    public static readonly int MaxLateMinutes = 60;

    /// <summary>"patient", "therapist" or null.</summary>
    public string? CancelledBy => Status switch
    {
        AppointmentStatus.CancelledByPatient => "patient",
        AppointmentStatus.CancelledByTherapist => "therapist",
        _ => null,
    };

    /// <summary>The therapist recorded that the patient did not come (P63).</summary>
    public bool Missed => Status == AppointmentStatus.NoShow;

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

    /// <summary>S18 cancel (SB-26): a reason from <see cref="CancelReasons.Therapist"/>, until the session's end.</summary>
    public void CancelByTherapist(string reasonCode, byte[]? noteCipher, IClock clock)
    {
        if (!CancelReasons.Therapist.Contains(reasonCode))
        {
            throw new ArgumentOutOfRangeException(nameof(reasonCode));
        }

        if (Status != AppointmentStatus.Confirmed || clock.UtcNow >= StartsAtUtc.AddMinutes(DurationMinutes))
        {
            throw new DomainException(ErrorCodes.AppointmentNotChangeable, $"Appointment {Id} is not confirmed or already over.");
        }

        Status = AppointmentStatus.CancelledByTherapist;
        CancelledAtUtc = clock.UtcNow;
        CancelReason = reasonCode;
        TherapistCancelNoteCipher = noteCipher;
    }

    /// <summary>
    /// S18 "running late": from the moment the lobby opens until the session's planned end. A new value replaces
    /// the old one, so the therapist can correct it.
    /// </summary>
    public void ReportLate(int minutes, IClock clock)
    {
        if (minutes < 1 || minutes > MaxLateMinutes)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes));
        }

        if (Status != AppointmentStatus.Confirmed || clock.UtcNow < StartsAtUtc - JoinOpensBefore || clock.UtcNow >= StartsAtUtc.AddMinutes(DurationMinutes))
        {
            throw new DomainException(ErrorCodes.AppointmentNotLateable, $"Appointment {Id} is not about to start.");
        }

        TherapistLateMinutes = minutes;
        TherapistLateAtUtc = clock.UtcNow;
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
