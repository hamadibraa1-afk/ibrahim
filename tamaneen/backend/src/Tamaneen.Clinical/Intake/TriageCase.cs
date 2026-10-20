using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Intake;

public enum TriageStatus
{
    Pending = 0,
    Reviewed = 1,
    Matched = 2,
}

/// <summary>
/// A patient's file waiting for a clinical supervisor (within 24 hours). Reviewing it produces the therapist
/// suggestions (P07); booking a session marks it matched.
/// </summary>
public sealed class TriageCase : Entity
{
    public static readonly TimeSpan ReviewWithin = TimeSpan.FromHours(24);

    private TriageCase(Guid id, Guid patientId, Guid? intakeId, string path, RiskLevel riskLevel, int? phq9, int? gad7, bool? cssrsPositive, DateTimeOffset now)
        : base(id)
    {
        PatientId = patientId;
        IntakeId = intakeId;
        Path = path;
        RiskLevel = riskLevel;
        Phq9 = phq9;
        Gad7 = gad7;
        CssrsPositive = cssrsPositive;
        SubmittedAtUtc = now;
        DueAtUtc = now + ReviewWithin;
    }

    // EF Core
    private TriageCase()
    {
        Path = "intake";
    }

    public Guid PatientId { get; private set; }

    public Guid? IntakeId { get; private set; }

    /// <summary>"intake" or "direct".</summary>
    public string Path { get; private set; }

    public TriageStatus Status { get; private set; }

    public RiskLevel RiskLevel { get; private set; }

    public int? Phq9 { get; private set; }

    public int? Gad7 { get; private set; }

    public bool? CssrsPositive { get; private set; }

    public DateTimeOffset SubmittedAtUtc { get; private set; }

    public DateTimeOffset DueAtUtc { get; private set; }

    public DateTimeOffset? ReviewedAtUtc { get; private set; }

    public Guid? ReviewedBy { get; private set; }

    /// <summary>Encrypted notes the patient added after submitting (P36), newline separated.</summary>
    public byte[]? AddendaCipher { get; private set; }

    public static TriageCase FromIntake(Guid patientId, Guid intakeId, RiskLevel riskLevel, int? phq9, int? gad7, IClock clock) =>
        new(Guid.NewGuid(), patientId, intakeId, "intake", riskLevel, phq9, gad7, null, clock.UtcNow);

    /// <summary>Direct booking (P22 "I know what I want"): the safety check replaces the intake.</summary>
    public static TriageCase Direct(Guid patientId, RiskLevel riskLevel, bool cssrsPositive, IClock clock)
    {
        var c = new TriageCase(Guid.NewGuid(), patientId, null, "direct", riskLevel, null, null, cssrsPositive, clock.UtcNow);
        return c;
    }

    public void RaiseRisk(RiskLevel level)
    {
        if (level > RiskLevel)
        {
            RiskLevel = level;
        }
    }

    public void Review(Guid supervisorId, RiskLevel riskLevel, IClock clock)
    {
        if (Status != TriageStatus.Pending)
        {
            throw new DomainException(ErrorCodes.TriageAlreadyReviewed, $"Triage {Id} was already reviewed.");
        }

        RiskLevel = riskLevel;
        ReviewedBy = supervisorId;
        ReviewedAtUtc = clock.UtcNow;
        Status = TriageStatus.Reviewed;
    }

    public void MarkMatched() => Status = TriageStatus.Matched;

    public void SetAddenda(byte[] cipher) => AddendaCipher = cipher;
}
