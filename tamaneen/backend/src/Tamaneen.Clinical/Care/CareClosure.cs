using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Care;

public enum CareClosureStatus
{
    Draft = 0,
    Submitted = 1,
    SignedOff = 2,
    Rejected = 3,
}

/// <summary>
/// End of care (S57, P53). The therapist writes the closing summary, a plain-language summary for the patient and a
/// relapse-prevention plan, and sends it to a supervisor. The supervisor's sign-off ends the care assignment; the
/// patient then sees P53. Closing is refused while the risk was 3 or 4 in the last 14 days.
/// </summary>
public sealed class CareClosure : Entity
{
    public const int MaxPatientSummary = 1500;
    public const int MaxPlanField = 300;

    public static readonly IReadOnlySet<string> Reasons = new HashSet<string>(StringComparer.Ordinal) { "goals_met", "patient_choice", "transferred", "no_contact", "other" };

    public static readonly IReadOnlySet<string> FollowUps = new HashSet<string>(StringComparer.Ordinal) { "none", "phq9_1m_3m" };

    private CareClosure(Guid id, Guid patientId, Guid therapistUserId, DateTimeOffset now)
        : base(id)
    {
        PatientId = patientId;
        TherapistUserId = therapistUserId;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
        ReasonCode = string.Empty;
        FollowUp = "none";
        KeepLibrary = true;
    }

    // EF Core
    private CareClosure()
    {
        ReasonCode = string.Empty;
        FollowUp = "none";
    }

    public Guid PatientId { get; private set; }

    public Guid TherapistUserId { get; private set; }

    public CareClosureStatus Status { get; private set; }

    public string ReasonCode { get; private set; }

    public int SessionCount { get; private set; }

    public int DurationWeeks { get; private set; }

    /// <summary>Encrypted plain-language summary the patient reads in P53 (no raw scores).</summary>
    public byte[]? PatientSummaryCipher { get; private set; }

    /// <summary>Encrypted JSON { warningSigns, whatToDo }.</summary>
    public byte[]? RelapsePlanCipher { get; private set; }

    public string FollowUp { get; private set; }

    public bool KeepLibrary { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? SubmittedAtUtc { get; private set; }

    public Guid? DecidedByUserId { get; private set; }

    public DateTimeOffset? DecidedAtUtc { get; private set; }

    /// <summary>Encrypted reason a supervisor gave when sending it back.</summary>
    public byte[]? RejectionCipher { get; private set; }

    public DateTimeOffset? ReturnRequestedAtUtc { get; private set; }

    public int Revision { get; private set; }

    public bool IsOpen => Status is CareClosureStatus.Draft or CareClosureStatus.Rejected or CareClosureStatus.Submitted;

    public static CareClosure Start(Guid patientId, Guid therapistUserId, IClock clock) => new(Guid.NewGuid(), patientId, therapistUserId, clock.UtcNow);

    public void Save(string reasonCode, int sessionCount, int durationWeeks, byte[]? patientSummaryCipher, byte[]? relapsePlanCipher, string followUp, bool keepLibrary, IClock clock)
    {
        if (Status is CareClosureStatus.Submitted or CareClosureStatus.SignedOff)
        {
            throw new DomainException(ErrorCodes.ClosureLocked, $"Closure {Id} is {Status}.");
        }

        if ((reasonCode.Length > 0 && !Reasons.Contains(reasonCode)) || !FollowUps.Contains(followUp) || sessionCount is < 0 or > 500 || durationWeeks is < 0 or > 520)
        {
            throw new DomainException(ErrorCodes.Validation, "Invalid closure values.");
        }

        ReasonCode = reasonCode;
        SessionCount = sessionCount;
        DurationWeeks = durationWeeks;
        PatientSummaryCipher = patientSummaryCipher;
        RelapsePlanCipher = relapsePlanCipher;
        FollowUp = followUp;
        KeepLibrary = keepLibrary;
        UpdatedAtUtc = clock.UtcNow;
        Status = CareClosureStatus.Draft;
        Revision++;
    }

    /// <summary>The caller checks the required fields and the 14-day risk rule first.</summary>
    public void Submit(IClock clock)
    {
        if (Status != CareClosureStatus.Draft)
        {
            throw new DomainException(ErrorCodes.ClosureLocked, $"Closure {Id} is {Status}.");
        }

        if (ReasonCode.Length == 0 || PatientSummaryCipher is null || RelapsePlanCipher is null)
        {
            throw new DomainException(ErrorCodes.Validation, "Reason, patient summary and relapse plan are required.");
        }

        Status = CareClosureStatus.Submitted;
        SubmittedAtUtc = clock.UtcNow;
        UpdatedAtUtc = clock.UtcNow;
        RejectionCipher = null;
        Revision++;
    }

    /// <summary>The therapist takes a submitted closure back to edit it.</summary>
    public void Withdraw(IClock clock)
    {
        if (Status != CareClosureStatus.Submitted)
        {
            throw new DomainException(ErrorCodes.ClosureLocked, $"Closure {Id} is {Status}.");
        }

        Status = CareClosureStatus.Draft;
        SubmittedAtUtc = null;
        UpdatedAtUtc = clock.UtcNow;
        Revision++;
    }

    public void SignOff(Guid supervisorId, IClock clock)
    {
        EnsureSubmitted(supervisorId);
        Status = CareClosureStatus.SignedOff;
        DecidedByUserId = supervisorId;
        DecidedAtUtc = clock.UtcNow;
        Revision++;
    }

    public void Reject(Guid supervisorId, byte[] reasonCipher, IClock clock)
    {
        EnsureSubmitted(supervisorId);
        Status = CareClosureStatus.Rejected;
        DecidedByUserId = supervisorId;
        DecidedAtUtc = clock.UtcNow;
        RejectionCipher = reasonCipher;
        Revision++;
    }

    /// <summary>P53 "I need to come back": recorded once; matching with the previous therapist reopens.</summary>
    public void RequestReturn(IClock clock) => ReturnRequestedAtUtc ??= clock.UtcNow;

    private void EnsureSubmitted(Guid supervisorId)
    {
        if (Status != CareClosureStatus.Submitted)
        {
            throw new DomainException(ErrorCodes.ClosureNotSubmitted, $"Closure {Id} is {Status}.");
        }

        if (supervisorId == TherapistUserId)
        {
            throw new DomainException(ErrorCodes.ClosureSignOffNotAllowed, "The author cannot sign off their own closure.");
        }
    }
}
