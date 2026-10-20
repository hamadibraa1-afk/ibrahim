namespace Tamaneen.Infrastructure.Auditing;

/// <summary>
/// One audit record. The table is an append-only SQL Server ledger table: rows can be inserted but
/// never updated or deleted, and the database keeps a cryptographic digest that proves it.
/// No patient content is stored here, only pseudonyms, actions and reasons.
/// </summary>
public sealed class AuditEntry
{
    public AuditEntry(DateTimeOffset occurredAtUtc, Guid? actorUserId, string? actorRole, string action, string entityType, string? entityId, string? patientPseudonymId, string? reason, string? correlationId)
    {
        OccurredAtUtc = occurredAtUtc;
        ActorUserId = actorUserId;
        ActorRole = actorRole;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        PatientPseudonymId = patientPseudonymId;
        Reason = reason;
        CorrelationId = correlationId;
    }

    // EF Core
    private AuditEntry()
    {
        Action = string.Empty;
        EntityType = string.Empty;
    }

    public long Id { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public string? ActorRole { get; private set; }

    public string Action { get; private set; }

    public string EntityType { get; private set; }

    public string? EntityId { get; private set; }

    public string? PatientPseudonymId { get; private set; }

    public string? Reason { get; private set; }

    public string? CorrelationId { get; private set; }
}
