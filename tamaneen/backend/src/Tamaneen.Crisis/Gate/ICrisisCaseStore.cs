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
}
