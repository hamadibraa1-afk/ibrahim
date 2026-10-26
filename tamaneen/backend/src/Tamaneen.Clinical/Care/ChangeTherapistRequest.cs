using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Care;

public enum ChangeTherapistStatus
{
    /// <summary>Open: the supervisor's triage case (path "change") decides who is offered.</summary>
    Requested = 0,

    /// <summary>The patient booked with a new therapist; the old assignment ended (Transfer).</summary>
    Accepted = 1,

    /// <summary>The supervisor closed or escalated the case instead of offering therapists.</summary>
    Closed = 2,
}

/// <summary>
/// P44: the patient asks for another therapist, with an optional reason from a fixed list and no free text. The
/// current therapist is never told the reason and only learns of the change once the new booking is confirmed. Row
/// level security keeps the table to the patient, supervisors, the clinical lead and the scheduling service.
/// </summary>
public sealed class ChangeTherapistRequest : Entity
{
    public static readonly IReadOnlyList<string> ReasonCodes = ["not_comfortable", "schedule", "gender", "specialty", "other"];

    private ChangeTherapistRequest(Guid id, Guid patientId, Guid fromTherapistUserId, string? reasonCode, Guid triageCaseId, DateTimeOffset now)
        : base(id)
    {
        PatientId = patientId;
        FromTherapistUserId = fromTherapistUserId;
        ReasonCode = reasonCode;
        TriageCaseId = triageCaseId;
        CreatedAtUtc = now;
    }

    // EF Core
    private ChangeTherapistRequest()
    {
    }

    public Guid PatientId { get; private set; }

    public Guid FromTherapistUserId { get; private set; }

    public string? ReasonCode { get; private set; }

    public Guid TriageCaseId { get; private set; }

    public ChangeTherapistStatus Status { get; private set; }

    public Guid? NewTherapistUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    public static ChangeTherapistRequest Create(Guid patientId, Guid fromTherapistUserId, string? reasonCode, Guid triageCaseId, IClock clock)
    {
        if (reasonCode is not null && !ReasonCodes.Contains(reasonCode))
        {
            throw new DomainException(ErrorCodes.Validation, $"Unknown change reason {reasonCode}.");
        }

        return new ChangeTherapistRequest(Guid.NewGuid(), patientId, fromTherapistUserId, reasonCode, triageCaseId, clock.UtcNow);
    }

    public void Accept(Guid newTherapistUserId, IClock clock)
    {
        if (Status != ChangeTherapistStatus.Requested)
        {
            return;
        }

        Status = ChangeTherapistStatus.Accepted;
        NewTherapistUserId = newTherapistUserId;
        ResolvedAtUtc = clock.UtcNow;
    }

    public void Close(IClock clock)
    {
        if (Status == ChangeTherapistStatus.Requested)
        {
            Status = ChangeTherapistStatus.Closed;
            ResolvedAtUtc = clock.UtcNow;
        }
    }
}
