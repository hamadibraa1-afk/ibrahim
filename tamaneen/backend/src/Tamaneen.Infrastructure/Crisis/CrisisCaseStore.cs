using Microsoft.EntityFrameworkCore;
using Tamaneen.Crisis;
using Tamaneen.Crisis.Gate;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.Infrastructure.Security;

namespace Tamaneen.Infrastructure.Crisis;

/// <summary>EF Core implementation of the gate's persistence. Every write joins the context's single SaveChanges.</summary>
public sealed class CrisisCaseStore : ICrisisCaseStore
{
    private readonly TamaneenDbContext _db;
    private readonly IAuditLog _audit;
    private readonly IPatientDataProtector _protector;

    public CrisisCaseStore(TamaneenDbContext db, IAuditLog audit, IPatientDataProtector protector)
    {
        _db = db;
        _audit = audit;
        _protector = protector;
    }

    public const string ExcerptField = "crisis.excerpt";

    public async Task AttachExcerptAsync(CrisisEvent crisisEvent, string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(crisisEvent);
        var excerpt = text.Length <= 2000 ? text : text[..2000];
        crisisEvent.SetExcerpt(await _protector.EncryptAsync(crisisEvent.PatientId, ExcerptField, excerpt, cancellationToken).ConfigureAwait(false));
    }

    public async Task<string?> FindPseudonymAsync(Guid patientId, CancellationToken cancellationToken) =>
        await _db.UserAccounts.Where(a => a.Id == patientId).Select(a => a.PseudonymId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    public Task<CrisisEvent?> FindOpenEventAsync(Guid patientId, CancellationToken cancellationToken) =>
        _db.CrisisEvents.FirstOrDefaultAsync(e => e.PatientId == patientId && e.Status != CrisisEventStatus.Closed, cancellationToken);

    public Task<ChatLock?> FindActiveLockAsync(Guid patientId, CancellationToken cancellationToken) =>
        _db.ChatLocks.FirstOrDefaultAsync(l => l.PatientId == patientId && l.ReleasedAtUtc == null, cancellationToken);

    public Task<CrisisEvent?> FindEventAsync(Guid eventId, CancellationToken cancellationToken) =>
        _db.CrisisEvents.FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);

    public void Add(CrisisEvent crisisEvent) => _db.CrisisEvents.Add(crisisEvent);

    public void Add(ChatLock chatLock) => _db.ChatLocks.Add(chatLock);

    public void Add(PageRequest pageRequest) => _db.PageRequests.Add(pageRequest);

    public void Audit(string action, CrisisEvent crisisEvent, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(crisisEvent);
        _audit.Append(action, "CrisisEvent", crisisEvent.Id.ToString(), crisisEvent.PseudonymId, reason ?? $"level={(int)crisisEvent.Level};rules={crisisEvent.TriggerRuleIds}");
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => _db.SaveChangesAsync(cancellationToken);
}
