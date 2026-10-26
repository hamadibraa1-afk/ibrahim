using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Intake;

public enum TriageStatus
{
    Pending = 0,
    Reviewed = 1,
    Matched = 2,

    /// <summary>The supervisor opened a crisis event instead of matching (S03 "escalate").</summary>
    Escalated = 3,

    /// <summary>Closed without matching; the patient sees the referral resources (S03 "close with referral").</summary>
    Closed = 4,
}

/// <summary>Result of a risk override: applied at once, or waiting for the clinical lead's co-sign.</summary>
public enum RiskChange
{
    Applied = 0,
    CoSignRequested = 1,
}

/// <summary>
/// A patient's file waiting for a clinical supervisor. The review deadline depends on the risk level (SlaPolicies:
/// 24 hours for level 1, 4 hours for level 2, one hour above). A supervisor claims the case for 15 minutes before
/// deciding (S03); approving it produces the therapist suggestions (P07); booking a session marks it matched.
/// </summary>
public sealed class TriageCase : Entity
{
    public static readonly TimeSpan ClaimFor = TimeSpan.FromMinutes(15);

    public const int MaxReasonLength = 1000;

    /// <summary>Where a closed case can send the patient instead of a therapist match.</summary>
    public static readonly IReadOnlySet<string> ReferralCodes = new HashSet<string>(StringComparer.Ordinal) { "crisis_line", "primary_care", "psychiatry", "community_support", "emergency_services" };

    private TriageCase(Guid id, Guid patientId, Guid? intakeId, string path, RiskLevel riskLevel, int? phq9, int? gad7, bool? cssrsPositive, DateTimeOffset now)
        : base(id)
    {
        PatientId = patientId;
        IntakeId = intakeId;
        Path = path;
        RiskLevel = riskLevel;
        RuleRiskLevel = riskLevel;
        Phq9 = phq9;
        Gad7 = gad7;
        CssrsPositive = cssrsPositive;
        SubmittedAtUtc = now;
        DueAtUtc = now + ReviewWithin(riskLevel);
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

    /// <summary>The level now: the rules' level, raised by later evidence or changed by a supervisor's decision.</summary>
    public RiskLevel RiskLevel { get; private set; }

    /// <summary>The level the rules set when the case was created; never changes.</summary>
    public RiskLevel RuleRiskLevel { get; private set; }

    public int? Phq9 { get; private set; }

    public int? Gad7 { get; private set; }

    public bool? CssrsPositive { get; private set; }

    public DateTimeOffset SubmittedAtUtc { get; private set; }

    public DateTimeOffset DueAtUtc { get; private set; }

    public DateTimeOffset? ReviewedAtUtc { get; private set; }

    public Guid? ReviewedBy { get; private set; }

    /// <summary>Encrypted notes the patient added after submitting (P36), newline separated.</summary>
    public byte[]? AddendaCipher { get; private set; }

    /// <summary>Supervisor holding the case (soft lock), until <see cref="ClaimedUntilUtc"/>.</summary>
    public Guid? ClaimedByUserId { get; private set; }

    public DateTimeOffset? ClaimedUntilUtc { get; private set; }

    /// <summary>A lowering from High or Critical that waits for the clinical lead's co-sign (EC-48).</summary>
    public RiskLevel? PendingRiskLevel { get; private set; }

    public Guid? PendingRiskRequestedBy { get; private set; }

    public DateTimeOffset? PendingRiskRequestedAtUtc { get; private set; }

    /// <summary>Comma-separated <see cref="ReferralCodes"/> chosen when the case was closed.</summary>
    public string? Referrals { get; private set; }

    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    /// <summary>Supervisor review deadline by level (SlaPolicies).</summary>
    public static TimeSpan ReviewWithin(RiskLevel level) => level switch
    {
        >= RiskLevel.High => TimeSpan.FromHours(1),
        RiskLevel.Elevated => TimeSpan.FromHours(4),
        _ => TimeSpan.FromHours(24),
    };

    /// <summary>Path of a case opened by a patient's change-of-therapist request (P44); it reuses the S03 queue.</summary>
    public const string ChangePath = "change";

    public bool IsChangeRequest => Path == ChangePath;

    /// <summary>
    /// P44: a supervisor proposes new therapists for a patient already in care. The case carries the clinical
    /// file's level, scores and intake (so S03 shows the summary) and is due within <paramref name="dueWithin"/>,
    /// or sooner when the level asks for it.
    /// </summary>
    public static TriageCase ForChange(TriageCase? clinical, Guid patientId, TimeSpan dueWithin, IClock clock)
    {
        var level = clinical?.RiskLevel ?? RiskLevel.Routine;
        var c = new TriageCase(Guid.NewGuid(), patientId, clinical?.IntakeId, ChangePath, level, clinical?.Phq9, clinical?.Gad7, clinical?.CssrsPositive, clock.UtcNow);
        var due = clock.UtcNow + dueWithin;
        if (due < c.DueAtUtc)
        {
            c.DueAtUtc = due;
        }

        return c;
    }

    public static TriageCase FromIntake(Guid patientId, Guid intakeId, RiskLevel riskLevel, int? phq9, int? gad7, IClock clock) =>
        new(Guid.NewGuid(), patientId, intakeId, "intake", riskLevel, phq9, gad7, null, clock.UtcNow);

    /// <summary>Direct booking (P22 "I know what I want"): the safety check replaces the intake.</summary>
    public static TriageCase Direct(Guid patientId, RiskLevel riskLevel, bool cssrsPositive, IClock clock)
    {
        var c = new TriageCase(Guid.NewGuid(), patientId, null, "direct", riskLevel, null, null, cssrsPositive, clock.UtcNow);
        return c;
    }

    /// <summary>True while a supervisor's decision can still make the patient see suggestions or referrals.</summary>
    public bool IsOpen => Status == TriageStatus.Pending;

    public bool IsClaimedBy(Guid userId, DateTimeOffset now) => ClaimedByUserId == userId && ClaimedUntilUtc > now;

    public bool IsClaimedByOther(Guid userId, DateTimeOffset now) => ClaimedByUserId is { } holder && holder != userId && ClaimedUntilUtc > now;

    /// <summary>New evidence (an addendum, a therapist's flag) only ever raises the level, and brings the deadline forward.</summary>
    public void RaiseRisk(RiskLevel level)
    {
        if (level > RiskLevel)
        {
            RiskLevel = level;
            var due = SubmittedAtUtc + ReviewWithin(level);
            if (due < DueAtUtc)
            {
                DueAtUtc = due;
            }
        }
    }

    /// <summary>Takes the case for 15 minutes (server time). Renewing your own claim extends it.</summary>
    public void Claim(Guid userId, IClock clock)
    {
        EnsureOpen();
        if (IsClaimedByOther(userId, clock.UtcNow))
        {
            throw new DomainException(ErrorCodes.TriageClaimedByOther, $"Triage {Id} is claimed by someone else.");
        }

        ClaimedByUserId = userId;
        ClaimedUntilUtc = clock.UtcNow + ClaimFor;
    }

    /// <summary>Releases the caller's own claim; anyone else's claim is left alone.</summary>
    public void Release(Guid userId)
    {
        if (ClaimedByUserId == userId)
        {
            ClaimedByUserId = null;
            ClaimedUntilUtc = null;
        }
    }

    /// <summary>
    /// A supervisor changes the level, with a reason. It can never go below <paramref name="floor"/> (an open or
    /// recent crisis). Lowering from High or Critical needs the clinical lead: a lead's own decision applies, anyone
    /// else's waits for the lead's co-sign.
    /// </summary>
    public RiskChange OverrideRisk(Guid userId, bool byClinicalLead, RiskLevel level, RiskLevel floor, string? reason, IClock clock)
    {
        EnsureDecision(userId, clock);
        RequireReason(reason);
        EnsureLevel(level, floor);
        if (level < RiskLevel && RiskLevel >= RiskLevel.High && !byClinicalLead)
        {
            PendingRiskLevel = level;
            PendingRiskRequestedBy = userId;
            PendingRiskRequestedAtUtc = clock.UtcNow;
            return RiskChange.CoSignRequested;
        }

        SetLevel(level);
        return RiskChange.Applied;
    }

    /// <summary>The clinical lead confirms or refuses a pending lowering. Nobody co-signs their own request.</summary>
    public RiskLevel CoSign(Guid leadUserId, bool approve, RiskLevel floor)
    {
        EnsureOpen();
        if (PendingRiskLevel is not { } pending)
        {
            throw new DomainException(ErrorCodes.TriageNoCoSignPending, $"Triage {Id} has no lowering to co-sign.");
        }

        if (PendingRiskRequestedBy == leadUserId)
        {
            throw new DomainException(ErrorCodes.TriageCoSignNotAllowed, $"Triage {Id}: the requester cannot co-sign.");
        }

        if (approve)
        {
            EnsureLevel(pending, floor);
            SetLevel(pending);
        }
        else
        {
            ClearPending();
        }

        return pending;
    }

    /// <summary>Approve: the level and the suggestions go to the patient.</summary>
    public void Approve(Guid userId, IClock clock)
    {
        EnsureDecision(userId, clock);
        if (PendingRiskLevel is not null)
        {
            throw new DomainException(ErrorCodes.TriageCoSignPending, $"Triage {Id} waits for a co-sign.");
        }

        Decide(TriageStatus.Reviewed, userId, clock);
    }

    /// <summary>Reassign keeps the case open: it only needs the claim (and a reason, checked by the caller).</summary>
    public void EnsureCanReassign(Guid userId, string? reason, IClock clock)
    {
        EnsureDecision(userId, clock);
        RequireReason(reason);
    }

    public void Escalate(Guid userId, RiskLevel level, string? reason, IClock clock)
    {
        EnsureDecision(userId, clock);
        RequireReason(reason);
        RaiseRisk(level);
        ClearPending();
        Decide(TriageStatus.Escalated, userId, clock);
    }

    public void Close(Guid userId, IReadOnlyCollection<string> referrals, string? reason, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(referrals);
        EnsureDecision(userId, clock);
        RequireReason(reason);
        if (referrals.Count == 0 || referrals.Any(r => !ReferralCodes.Contains(r)))
        {
            throw new DomainException(ErrorCodes.Validation, "Choose at least one known referral.");
        }

        if (PendingRiskLevel is not null)
        {
            throw new DomainException(ErrorCodes.TriageCoSignPending, $"Triage {Id} waits for a co-sign.");
        }

        Referrals = string.Join(',', referrals.Distinct(StringComparer.Ordinal));
        Decide(TriageStatus.Closed, userId, clock);
    }

    /// <summary>
    /// Older single-step review (POST /v1/staff/triage/{id}/review). It can confirm or raise the level but never
    /// lower it: lowering needs a reason and, from High or Critical, a co-sign, which only a decision carries.
    /// </summary>
    public void Review(Guid supervisorId, RiskLevel riskLevel, RiskLevel floor, IClock clock)
    {
        EnsureOpen();
        if (riskLevel < RiskLevel)
        {
            throw new DomainException(ErrorCodes.TriageLoweringNeedsDecision, $"Triage {Id}: lowering needs a decision with a reason.");
        }

        EnsureLevel(riskLevel, floor);
        if (IsClaimedByOther(supervisorId, clock.UtcNow))
        {
            throw new DomainException(ErrorCodes.TriageClaimedByOther, $"Triage {Id} is claimed by someone else.");
        }

        SetLevel(riskLevel);
        Decide(TriageStatus.Reviewed, supervisorId, clock);
    }

    public void MarkMatched()
    {
        if (Status is TriageStatus.Pending or TriageStatus.Reviewed)
        {
            Status = TriageStatus.Matched;
        }
    }

    /// <summary>
    /// Direct-path file (no intake): the treating therapist's approval is its clinical review. It may come
    /// after the booking has already marked the case matched.
    /// </summary>
    public void ApproveByTherapist(Guid therapistUserId, IClock clock)
    {
        ReviewedAtUtc ??= clock.UtcNow;
        ReviewedBy ??= therapistUserId;
        if (Status == TriageStatus.Pending)
        {
            Status = TriageStatus.Reviewed;
        }
    }

    public void SetAddenda(byte[] cipher) => AddendaCipher = cipher;

    private void EnsureOpen()
    {
        if (Status != TriageStatus.Pending)
        {
            throw new DomainException(ErrorCodes.TriageAlreadyReviewed, $"Triage {Id} was already reviewed.");
        }
    }

    /// <summary>No decision without an active claim (EC-49).</summary>
    private void EnsureDecision(Guid userId, IClock clock)
    {
        EnsureOpen();
        if (IsClaimedByOther(userId, clock.UtcNow))
        {
            throw new DomainException(ErrorCodes.TriageClaimedByOther, $"Triage {Id} is claimed by someone else.");
        }

        if (!IsClaimedBy(userId, clock.UtcNow))
        {
            throw new DomainException(ErrorCodes.TriageClaimRequired, $"Triage {Id} must be claimed first.");
        }
    }

    private static void RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(ErrorCodes.ReasonRequired, "A reason is required for this decision.");
        }

        if (reason.Trim().Length > MaxReasonLength)
        {
            throw new DomainException(ErrorCodes.Validation, "The reason is too long.");
        }
    }

    private static void EnsureLevel(RiskLevel level, RiskLevel floor)
    {
        if (level is < RiskLevel.Routine or > RiskLevel.Critical)
        {
            throw new DomainException(ErrorCodes.Validation, $"Unknown risk level {level}.");
        }

        if (level < floor)
        {
            throw new DomainException(ErrorCodes.TriageBelowCrisisFloor, $"Level {level} is below the crisis floor {floor}.", (int)floor);
        }
    }

    private void SetLevel(RiskLevel level)
    {
        RiskLevel = level;
        DueAtUtc = SubmittedAtUtc + ReviewWithin(level);
        ClearPending();
    }

    private void ClearPending()
    {
        PendingRiskLevel = null;
        PendingRiskRequestedBy = null;
        PendingRiskRequestedAtUtc = null;
    }

    private void Decide(TriageStatus status, Guid userId, IClock clock)
    {
        Status = status;
        ReviewedBy = userId;
        ReviewedAtUtc = clock.UtcNow;
        ClaimedByUserId = null;
        ClaimedUntilUtc = null;
    }
}
