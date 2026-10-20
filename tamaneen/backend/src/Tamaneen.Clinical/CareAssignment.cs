using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical;

/// <summary>
/// Links a therapist to a patient. Row-level security reads this table: a therapist can see a patient's
/// rows only while an active assignment exists.
/// </summary>
public sealed class CareAssignment : Entity
{
    private CareAssignment(Guid id, Guid patientId, Guid therapistUserId, DateTimeOffset startedAtUtc)
        : base(id)
    {
        PatientId = patientId;
        TherapistUserId = therapistUserId;
        StartedAtUtc = startedAtUtc;
    }

    // EF Core
    private CareAssignment()
    {
        EndReason = null;
    }

    public Guid PatientId { get; private set; }

    public Guid TherapistUserId { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }

    public DateTimeOffset? EndedAtUtc { get; private set; }

    public string? EndReason { get; private set; }

    public bool IsActive => EndedAtUtc is null;

    public static CareAssignment Start(Guid patientId, Guid therapistUserId, IClock clock)
    {
        if (patientId == Guid.Empty || therapistUserId == Guid.Empty)
        {
            throw new ArgumentException("Patient and therapist ids are required.");
        }

        if (patientId == therapistUserId)
        {
            throw new ArgumentException("A user cannot be assigned as their own therapist.");
        }

        return new CareAssignment(Guid.NewGuid(), patientId, therapistUserId, clock.UtcNow);
    }

    public void End(string reason, IClock clock)
    {
        if (!IsActive)
        {
            throw new DomainException(ErrorCodes.AssignmentAlreadyEnded, $"Assignment {Id} already ended.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(ErrorCodes.ReasonRequired, "A reason is required to end an assignment.");
        }

        EndReason = reason.Trim();
        EndedAtUtc = clock.UtcNow;
    }
}
