using Tamaneen.SharedKernel;

namespace Tamaneen.Crisis;

public enum CrisisEventStatus
{
    Open = 0,
    Acknowledged = 1,
    Closed = 2,

    /// <summary>The owner reached the patient ("تم التواصل"); still owned and open until closed with an outcome.</summary>
    InContact = 3,
}

/// <summary>
/// A High or Critical risk event. It is opened by Crisis.Worker and owned by the on-call clinician
/// who acknowledges it. Timers are stored on the row so a worker restart loses nothing.
/// </summary>
public sealed class CrisisEvent : Entity
{
    private CrisisEvent(Guid id, Guid patientId, string pseudonymId, RiskLevel level, string language, string rulePackVersion, DateTimeOffset openedAtUtc, DateTimeOffset ackDeadlineUtc)
        : base(id)
    {
        PatientId = patientId;
        PseudonymId = pseudonymId;
        Level = level;
        Language = language;
        RulePackVersion = rulePackVersion;
        OpenedAtUtc = openedAtUtc;
        AckDeadlineUtc = ackDeadlineUtc;
        Status = CrisisEventStatus.Open;
    }

    // EF Core
    private CrisisEvent()
    {
        PseudonymId = string.Empty;
        Language = SupportedLanguages.Default;
        RulePackVersion = string.Empty;
    }

    public Guid PatientId { get; private set; }

    public string PseudonymId { get; private set; }

    public RiskLevel Level { get; private set; }

    public CrisisEventStatus Status { get; private set; }

    /// <summary>The patient's language, so alerts and scripts use it.</summary>
    public string Language { get; private set; }

    public string RulePackVersion { get; private set; }

    public DateTimeOffset OpenedAtUtc { get; private set; }

    public DateTimeOffset AckDeadlineUtc { get; private set; }

    public Guid? AcknowledgedByUserId { get; private set; }

    public DateTimeOffset? AcknowledgedAtUtc { get; private set; }

    public Guid? ClosedByUserId { get; private set; }

    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public string? CloseReason { get; private set; }

    /// <summary>0 = on-call clinician, 1 = backup, 2 = clinical lead. Rises each time an acknowledgement deadline passes.</summary>
    public byte EscalationTier { get; private set; }

    /// <summary>Deadline to reach the patient once someone has acknowledged (5 min for Critical, 60 for High).</summary>
    public DateTimeOffset? ContactDeadlineUtc { get; private set; }

    /// <summary>Comma-separated rule ids that triggered or raised the event. Never patient text.</summary>
    public string TriggerRuleIds { get; private set; } = string.Empty;

    /// <summary>Where the words came from: intake, chat, journal, survey, questionnaire, system.</summary>
    public string Source { get; private set; } = "system";

    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    /// <summary>Human-friendly number from a database sequence, shown as "CE-1042".</summary>
    public int Number { get; private set; }

    /// <summary>The words that triggered the event, encrypted with the patient's key; shown only after step-up.</summary>
    public byte[]? ExcerptCipher { get; private set; }

    /// <summary>safe_contacted, emergency_services, unreachable_referred or false_alarm.</summary>
    public string? CloseOutcome { get; private set; }

    public static readonly IReadOnlySet<string> CloseOutcomes = new HashSet<string>(StringComparer.Ordinal) { "safe_contacted", "emergency_services", "unreachable_referred", "false_alarm" };

    /// <summary>Closing as unreachable needs the clinical lead's signature and leaves a follow-up review (EC-33, SB-44).</summary>
    public const string UnreachableOutcome = "unreachable_referred";

    public static readonly TimeSpan UnreachableReviewWithin = TimeSpan.FromHours(72);

    /// <summary>The colleague a handover was offered to; ownership moves only when they accept (SB-43).</summary>
    public Guid? HandoverToUserId { get; private set; }

    public Guid? HandoverFromUserId { get; private set; }

    public DateTimeOffset? HandoverRequestedAtUtc { get; private set; }

    /// <summary>A supervisor's "unreachable" close waiting for the clinical lead's co-sign.</summary>
    public string? PendingCloseOutcome { get; private set; }

    public string? PendingCloseReason { get; private set; }

    public Guid? PendingCloseByUserId { get; private set; }

    public DateTimeOffset? PendingCloseAtUtc { get; private set; }

    public Guid? CoSignedByUserId { get; private set; }

    /// <summary>When an unreachable close must be reviewed (72 hours after closing). Shown on the timeline.</summary>
    public DateTimeOffset? ReviewDueAtUtc { get; private set; }

    /// <summary>Acknowledged or in contact: one person owns the event.</summary>
    public bool IsOwned => Status is CrisisEventStatus.Acknowledged or CrisisEventStatus.InContact;

    public void SetExcerpt(byte[] cipher) => ExcerptCipher = cipher;

    /// <summary>
    /// One owner per event (EC-35): once someone took it, only that person, a supervisor or the clinical lead act
    /// on it; others read. Before anyone took it, every responder who can see it may act, so nothing urgent waits.
    /// </summary>
    public bool CanAct(Guid userId, string? role) =>
        role is AppRoles.Supervisor or AppRoles.ClinicalLead || !IsOwned || AcknowledgedByUserId == userId;

    public void EnsureCanAct(Guid userId, string? role)
    {
        EnsureNotClosed();
        if (!CanAct(userId, role))
        {
            throw new DomainException(ErrorCodes.CrisisNotOwner, $"Crisis event {Id} is owned by someone else.");
        }
    }

    /// <summary>The owner reached the patient (SB-40).</summary>
    public void MarkInContact()
    {
        EnsureNotClosed();
        if (!IsOwned)
        {
            throw new DomainException(ErrorCodes.CrisisNotAcknowledged, $"Crisis event {Id} is not acknowledged.");
        }

        Status = CrisisEventStatus.InContact;
    }

    /// <summary>The responder hands the case back to the on-call queue: it is open again with a fresh deadline.</summary>
    public void HandOver(TimeSpan acknowledgeWithin, IClock clock)
    {
        if (!IsOwned)
        {
            throw new DomainException(ErrorCodes.CrisisNotAcknowledged, $"Crisis event {Id} is not acknowledged.");
        }

        Status = CrisisEventStatus.Open;
        AcknowledgedByUserId = null;
        AcknowledgedAtUtc = null;
        ContactDeadlineUtc = null;
        ClearHandover();
        AckDeadlineUtc = clock.UtcNow + acknowledgeWithin;
    }

    /// <summary>Offers the case to a colleague. The current owner keeps it until they accept (SB-43).</summary>
    public void RequestHandover(Guid fromUserId, Guid toUserId, IClock clock)
    {
        EnsureNotClosed();
        if (!IsOwned)
        {
            throw new DomainException(ErrorCodes.CrisisNotAcknowledged, $"Crisis event {Id} is not acknowledged.");
        }

        if (toUserId == AcknowledgedByUserId)
        {
            throw new DomainException(ErrorCodes.Validation, "The case cannot be handed over to its owner.");
        }

        HandoverToUserId = toUserId;
        HandoverFromUserId = fromUserId;
        HandoverRequestedAtUtc = clock.UtcNow;
    }

    /// <summary>The colleague the case was offered to takes it over.</summary>
    public void AcceptHandover(Guid userId)
    {
        EnsureNotClosed();
        if (HandoverToUserId is null || HandoverToUserId != userId || !IsOwned)
        {
            throw new DomainException(ErrorCodes.CrisisNothingPending, $"No handover of {Id} is waiting for {userId}.");
        }

        AcknowledgedByUserId = userId;
        ClearHandover();
    }

    /// <summary>The colleague declines, or the owner withdraws the offer. Ownership does not change.</summary>
    public void CancelHandover(Guid userId, string? role)
    {
        if (HandoverToUserId is null || (userId != HandoverToUserId && !CanAct(userId, role)))
        {
            throw new DomainException(ErrorCodes.CrisisNothingPending, $"No handover of {Id} is waiting.");
        }

        ClearHandover();
    }

    /// <summary>
    /// Closes with an outcome. A supervisor's "unreachable" close is held for the clinical lead's co-sign and
    /// returns false; everything else closes now and returns true.
    /// </summary>
    public bool Close(Guid userId, string role, string outcome, string reason, IClock clock)
    {
        if (!CloseOutcomes.Contains(outcome))
        {
            throw new DomainException(ErrorCodes.Validation, $"Unknown close outcome '{outcome}'.");
        }

        if (outcome == UnreachableOutcome && role != AppRoles.ClinicalLead)
        {
            EnsureCloser(role, reason);
            PendingCloseOutcome = outcome;
            PendingCloseReason = reason.Trim();
            PendingCloseByUserId = userId;
            PendingCloseAtUtc = clock.UtcNow;
            return false;
        }

        Close(userId, role, reason, clock);
        CloseOutcome = outcome;
        if (outcome == UnreachableOutcome)
        {
            CoSignedByUserId = userId;
            ReviewDueAtUtc = clock.UtcNow + UnreachableReviewWithin;
        }

        return true;
    }

    /// <summary>The clinical lead signs a supervisor's "unreachable" close; it closes now with a 72-hour review.</summary>
    public void CoSignClose(Guid leadUserId, string role, IClock clock)
    {
        if (role != AppRoles.ClinicalLead)
        {
            throw new DomainException(ErrorCodes.CrisisCloseNotAllowed, $"Role {role} cannot co-sign crisis closes.");
        }

        if (PendingCloseOutcome is null)
        {
            throw new DomainException(ErrorCodes.CrisisNothingPending, $"No close of {Id} waits for a co-sign.");
        }

        var (outcome, reason, by) = (PendingCloseOutcome, PendingCloseReason!, PendingCloseByUserId!.Value);
        Close(by, AppRoles.Supervisor, reason, clock);
        CloseOutcome = outcome;
        CoSignedByUserId = leadUserId;
        ReviewDueAtUtc = clock.UtcNow + UnreachableReviewWithin;
    }

    private void ClearHandover()
    {
        HandoverToUserId = null;
        HandoverFromUserId = null;
        HandoverRequestedAtUtc = null;
    }

    private void EnsureNotClosed()
    {
        if (Status == CrisisEventStatus.Closed)
        {
            throw new DomainException(ErrorCodes.CrisisClosed, $"Crisis event {Id} is closed.");
        }
    }

    private static void EnsureCloser(string role, string reason)
    {
        if (role is not (AppRoles.Supervisor or AppRoles.ClinicalLead))
        {
            throw new DomainException(ErrorCodes.CrisisCloseNotAllowed, $"Role {role} cannot close crisis events.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(ErrorCodes.ReasonRequired, "A reason is required to close a crisis event.");
        }
    }

    public static CrisisEvent Open(Guid patientId, string pseudonymId, RiskLevel level, string language, string rulePackVersion, TimeSpan acknowledgeWithin, IClock clock) =>
        Open(patientId, pseudonymId, level, language, rulePackVersion, acknowledgeWithin, Array.Empty<string>(), "system", clock);

    public static CrisisEvent Open(Guid patientId, string pseudonymId, RiskLevel level, string language, string rulePackVersion, TimeSpan acknowledgeWithin, IEnumerable<string> ruleIds, string source, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(ruleIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (level < RiskLevel.High)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Crisis events are opened for High or Critical risk only.");
        }

        if (acknowledgeWithin <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(acknowledgeWithin), "The acknowledgement window must be positive.");
        }

        var now = clock.UtcNow;
        var evt = new CrisisEvent(Guid.NewGuid(), patientId, SharedKernel.PseudonymId.Parse(pseudonymId).Value, level, SupportedLanguages.Normalize(language), rulePackVersion, now, now + acknowledgeWithin)
        {
            Source = source.Trim().ToLowerInvariant(),
        };
        evt.AddTriggers(ruleIds);
        return evt;
    }

    public void AddTriggers(IEnumerable<string> ruleIds)
    {
        ArgumentNullException.ThrowIfNull(ruleIds);
        var all = TriggerRuleIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Concat(ruleIds)
            .Select(r => r.Trim()).Where(r => r.Length > 0).Distinct(StringComparer.Ordinal);
        var joined = string.Join(',', all);
        TriggerRuleIds = joined.Length <= 1000 ? joined : joined[..1000];
    }

    /// <summary>
    /// Called when the acknowledgement deadline has passed: pages the next tier and gives it a fresh window.
    /// After the clinical lead (tier 2) the lead keeps being paged on every window until someone acknowledges.
    /// </summary>
    public byte EscalateToNextTier(TimeSpan acknowledgeWithin, byte maxTier, IClock clock)
    {
        if (Status != CrisisEventStatus.Open)
        {
            throw new InvalidOperationException($"Only open events escalate; {Id} is {Status}.");
        }

        if (EscalationTier < maxTier)
        {
            EscalationTier++;
        }

        AckDeadlineUtc = clock.UtcNow + acknowledgeWithin;
        return EscalationTier;
    }

    public bool IsAcknowledgementOverdue(IClock clock) =>
        Status == CrisisEventStatus.Open && clock.UtcNow > AckDeadlineUtc;

    /// <summary>Raises the level when new evidence is worse. A level is never lowered.</summary>
    public void Escalate(RiskLevel level)
    {
        if (level > Level)
        {
            Level = level;
        }
    }

    public void Acknowledge(Guid clinicianUserId, IClock clock) => Acknowledge(clinicianUserId, null, clock);

    public void Acknowledge(Guid clinicianUserId, TimeSpan? contactWithin, IClock clock)
    {
        switch (Status)
        {
            case CrisisEventStatus.Acknowledged or CrisisEventStatus.InContact:
                throw new DomainException(ErrorCodes.CrisisAlreadyAcknowledged, $"Crisis event {Id} is already acknowledged.");
            case CrisisEventStatus.Closed:
                throw new DomainException(ErrorCodes.CrisisClosed, $"Crisis event {Id} is closed.");
        }

        AcknowledgedByUserId = clinicianUserId;
        AcknowledgedAtUtc = clock.UtcNow;
        ContactDeadlineUtc = contactWithin is { } within ? clock.UtcNow + within : null;
        Status = CrisisEventStatus.Acknowledged;
    }

    /// <summary>Only a supervisor or the clinical lead may close an event, and always with a reason.</summary>
    public void Close(Guid userId, string role, string reason, IClock clock)
    {
        EnsureNotClosed();
        EnsureCloser(role, reason);
        ClosedByUserId = userId;
        ClosedAtUtc = clock.UtcNow;
        CloseReason = reason.Trim();
        Status = CrisisEventStatus.Closed;
        ClearHandover();
        PendingCloseOutcome = null;
        PendingCloseReason = null;
        PendingCloseByUserId = null;
        PendingCloseAtUtc = null;
    }
}
