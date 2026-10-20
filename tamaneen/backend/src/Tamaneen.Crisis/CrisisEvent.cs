using Tamaneen.SharedKernel;

namespace Tamaneen.Crisis;

public enum CrisisEventStatus
{
    Open = 0,
    Acknowledged = 1,
    Closed = 2,
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
            case CrisisEventStatus.Acknowledged:
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
        if (Status == CrisisEventStatus.Closed)
        {
            throw new DomainException(ErrorCodes.CrisisClosed, $"Crisis event {Id} is closed.");
        }

        if (role is not (AppRoles.Supervisor or AppRoles.ClinicalLead))
        {
            throw new DomainException(ErrorCodes.CrisisCloseNotAllowed, $"Role {role} cannot close crisis events.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(ErrorCodes.ReasonRequired, "A reason is required to close a crisis event.");
        }

        ClosedByUserId = userId;
        ClosedAtUtc = clock.UtcNow;
        CloseReason = reason.Trim();
        Status = CrisisEventStatus.Closed;
    }
}
