using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamaneen.Crisis.Gate;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;

namespace Tamaneen.Crisis.Worker.Escalation;

/// <summary>
/// One pass of the escalation loop: every open event whose acknowledgement deadline has passed moves to the
/// next tier (on-call → backup → clinical lead), gets a fresh window and a new page. Rows are locked with
/// UPDLOCK, READPAST so several worker instances never escalate the same event twice.
/// </summary>
public sealed partial class EscalationService
{
    private readonly TamaneenDbContext _db;
    private readonly IAuditLog _audit;
    private readonly ICrisisAlertPublisher _alerts;
    private readonly CrisisSlaOptions _sla;
    private readonly IClock _clock;
    private readonly ILogger<EscalationService> _log;

    public EscalationService(TamaneenDbContext db, IAuditLog audit, ICrisisAlertPublisher alerts, IOptions<CrisisSlaOptions> sla, IClock clock, ILogger<EscalationService> log)
    {
        _db = db;
        _audit = audit;
        _alerts = alerts;
        _sla = sla.Value;
        _clock = clock;
        _log = log;
    }

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var overdue = new List<CrisisEvent>();

        // Retried as a unit on transient SQL errors; each attempt starts from a clean change tracker.
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async ct =>
        {
            _db.ChangeTracker.Clear();
            await using var tx = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            overdue = await _db.CrisisEvents
                .FromSql($"SELECT TOP (50) * FROM [crisis].[CrisisEvents] WITH (UPDLOCK, READPAST, ROWLOCK) WHERE [Status] = 0 AND [AckDeadlineUtc] < {now} ORDER BY [Level] DESC, [AckDeadlineUtc]")
                .ToListAsync(ct).ConfigureAwait(false);
            foreach (var evt in overdue)
            {
                var tier = evt.EscalateToNextTier(_sla.AcknowledgeWithin(evt.Level), _sla.MaxEscalationTier, _clock);
                _db.PageRequests.Add(PageRequest.For(evt, _clock));
                _audit.Append("crisis.escalated", "CrisisEvent", evt.Id.ToString(), evt.PseudonymId, $"tier={tier}");
                LogEscalated(_log, evt.Id, tier);
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        foreach (var evt in overdue)
        {
            try
            {
                await _alerts.EventUpdatedAsync(evt, CrisisUpdateKind.Escalated, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogAlertFailed(_log, evt.Id, ex);
            }
        }

        return overdue.Count;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Crisis event {EventId} was not acknowledged in time; escalated to tier {Tier}")]
    private static partial void LogEscalated(ILogger logger, Guid eventId, byte tier);

    [LoggerMessage(Level = LogLevel.Error, Message = "Live escalation alert for {EventId} failed; the page is queued")]
    private static partial void LogAlertFailed(ILogger logger, Guid eventId, Exception exception);
}
