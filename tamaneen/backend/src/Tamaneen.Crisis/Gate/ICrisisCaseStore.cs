namespace Tamaneen.Crisis.Gate;

/// <summary>Persistence the gate needs. One <see cref="SaveChangesAsync"/> commits event, lock, pages and audit together.</summary>
public interface ICrisisCaseStore
{
    Task<string?> FindPseudonymAsync(Guid patientId, CancellationToken cancellationToken);

    Task<CrisisEvent?> FindOpenEventAsync(Guid patientId, CancellationToken cancellationToken);

    Task<ChatLock?> FindActiveLockAsync(Guid patientId, CancellationToken cancellationToken);

    Task<CrisisEvent?> FindEventAsync(Guid eventId, CancellationToken cancellationToken);

    void Add(CrisisEvent crisisEvent);

    void Add(ChatLock chatLock);

    void Add(PageRequest pageRequest);

    /// <summary>Encrypts the triggering words (at most 2000 characters) onto the event with the patient's key.</summary>
    Task AttachExcerptAsync(CrisisEvent crisisEvent, string text, CancellationToken cancellationToken);

    /// <summary>Adds a content-free audit row to the same unit of work.</summary>
    void Audit(string action, CrisisEvent crisisEvent, string? reason = null);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Pushes live updates to the crisis console. Failures never undo the saved event: paging covers it.</summary>
public interface ICrisisAlertPublisher
{
    Task EventOpenedAsync(CrisisEvent crisisEvent, CancellationToken cancellationToken);

    Task EventUpdatedAsync(CrisisEvent crisisEvent, CrisisUpdateKind kind, CancellationToken cancellationToken);
}

public enum CrisisUpdateKind
{
    Raised = 0,
    Escalated = 1,
    Acknowledged = 2,
    Closed = 3,

    /// <summary>The responder handed the case back to the on-call queue.</summary>
    HandedOver = 4,

    /// <summary>Anything else that changes what the console shows: in contact, a handover offer or acceptance, a close waiting for co-sign.</summary>
    Updated = 5,
}
