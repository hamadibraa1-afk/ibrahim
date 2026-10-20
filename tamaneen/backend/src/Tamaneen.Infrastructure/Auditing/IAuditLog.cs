using System.Diagnostics;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;

namespace Tamaneen.Infrastructure.Auditing;

/// <summary>Adds audit rows to the current unit of work, so the audit row commits with the change it describes.</summary>
public interface IAuditLog
{
    void Append(string action, string entityType, string? entityId, string? patientPseudonymId = null, string? reason = null);
}

public sealed class AuditLog : IAuditLog
{
    private readonly TamaneenDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public AuditLog(TamaneenDbContext db, ICurrentUser user, IClock clock)
    {
        _db = db;
        _user = user;
        _clock = clock;
    }

    public void Append(string action, string entityType, string? entityId, string? patientPseudonymId = null, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        _db.AuditEntries.Add(new AuditEntry(
            _clock.UtcNow,
            _user.UserId,
            _user.Role,
            action,
            entityType,
            entityId,
            patientPseudonymId,
            reason,
            Activity.Current?.TraceId.ToString()));
    }
}
