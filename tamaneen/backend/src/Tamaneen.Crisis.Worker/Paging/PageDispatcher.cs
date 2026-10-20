using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;

namespace Tamaneen.Crisis.Worker.Paging;

/// <summary>Sends due pages from the outbox. A failed page stays in the outbox and is retried within a minute.</summary>
public sealed partial class PageDispatcher
{
    private readonly TamaneenDbContext _db;
    private readonly IPagerChannel _channel;
    private readonly WorkerOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<PageDispatcher> _log;

    public PageDispatcher(TamaneenDbContext db, IPagerChannel channel, IOptions<WorkerOptions> options, IClock clock, ILogger<PageDispatcher> log)
    {
        _db = db;
        _channel = channel;
        _options = options.Value;
        _clock = clock;
        _log = log;
    }

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var sent = 0;

        // Retried as a unit on transient SQL errors. A retry can resend a page already delivered;
        // receivers de-duplicate by PageId, and a duplicate page is far better than a lost one.
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async ct =>
        {
            _db.ChangeTracker.Clear();
            sent = 0;
            await using var tx = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var due = await _db.PageRequests
                .FromSql($"SELECT TOP (20) * FROM [crisis].[PageRequests] WITH (UPDLOCK, READPAST, ROWLOCK) WHERE [SentAtUtc] IS NULL AND [NextAttemptAtUtc] <= {now} ORDER BY [Level] DESC, [CreatedAtUtc]")
                .ToListAsync(ct).ConfigureAwait(false);

            foreach (var page in due)
            {
                var message = new PageMessage(page.Id, page.CrisisEventId, page.PseudonymId, (int)page.Level, page.Tier, page.PatientLanguage,
                    $"{_options.ConsoleBaseUrl.TrimEnd('/')}/crisis/{page.CrisisEventId}", page.CreatedAtUtc);
                try
                {
                    await _channel.SendAsync(message, ct).ConfigureAwait(false);
                    page.MarkSent(_clock);
                    sent++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    page.MarkFailed(ex.Message, _clock);
                    LogPageFailed(_log, page.Id, page.CrisisEventId, page.Attempts, ex);
                }
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        return sent;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Page {PageId} for crisis event {EventId} failed (attempt {Attempts}); will retry")]
    private static partial void LogPageFailed(ILogger logger, Guid pageId, Guid eventId, int attempts, Exception exception);
}
