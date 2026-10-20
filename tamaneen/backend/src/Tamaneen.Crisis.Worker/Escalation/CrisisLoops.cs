using System.Collections.Concurrent;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Tamaneen.Crisis.Worker.Paging;
using Tamaneen.Hosting;
using Tamaneen.SharedKernel;

namespace Tamaneen.Crisis.Worker.Escalation;

/// <summary>Last successful run of each loop. A loop that stops running makes the worker unhealthy, which pages engineers.</summary>
public sealed class LoopHeartbeat
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _beats = new(StringComparer.Ordinal);

    public void Beat(string loop, DateTimeOffset at) => _beats[loop] = at;

    public DateTimeOffset? Last(string loop) => _beats.TryGetValue(loop, out var at) ? at : null;
}

public sealed partial class EscalationLoop : BackgroundService
{
    public const string Name = "escalation";

    private readonly IServiceScopeFactory _scopes;
    private readonly WorkerOptions _options;
    private readonly LoopHeartbeat _heartbeat;
    private readonly IClock _clock;
    private readonly ILogger<EscalationLoop> _log;

    public EscalationLoop(IServiceScopeFactory scopes, IOptions<WorkerOptions> options, LoopHeartbeat heartbeat, IClock clock, ILogger<EscalationLoop> log)
    {
        _scopes = scopes;
        _options = options.Value;
        _heartbeat = heartbeat;
        _clock = clock;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.EscalationIntervalSeconds));
        do
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<RequestCurrentUser>().ActAsService(_options.ServiceUserId, AppRoles.CrisisService);
                await scope.ServiceProvider.GetRequiredService<EscalationService>().RunOnceAsync(stoppingToken).ConfigureAwait(false);
                _heartbeat.Beat(Name, _clock.UtcNow);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(_log, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Escalation pass failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

public sealed partial class PagingLoop : BackgroundService
{
    public const string Name = "paging";

    private readonly IServiceScopeFactory _scopes;
    private readonly WorkerOptions _options;
    private readonly LoopHeartbeat _heartbeat;
    private readonly IClock _clock;
    private readonly ILogger<PagingLoop> _log;

    public PagingLoop(IServiceScopeFactory scopes, IOptions<WorkerOptions> options, LoopHeartbeat heartbeat, IClock clock, ILogger<PagingLoop> log)
    {
        _scopes = scopes;
        _options = options.Value;
        _heartbeat = heartbeat;
        _clock = clock;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PagingIntervalSeconds));
        do
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<RequestCurrentUser>().ActAsService(_options.ServiceUserId, AppRoles.CrisisService);
                await scope.ServiceProvider.GetRequiredService<PageDispatcher>().RunOnceAsync(stoppingToken).ConfigureAwait(false);
                _heartbeat.Beat(Name, _clock.UtcNow);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(_log, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Paging pass failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

/// <summary>Unhealthy when a loop has not completed a pass within three of its intervals.</summary>
public sealed class LoopsHealthCheck : IHealthCheck
{
    private readonly LoopHeartbeat _heartbeat;
    private readonly WorkerOptions _options;
    private readonly IClock _clock;

    public LoopsHealthCheck(LoopHeartbeat heartbeat, IOptions<WorkerOptions> options, IClock clock)
    {
        _heartbeat = heartbeat;
        _options = options.Value;
        _clock = clock;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!_options.RunBackgroundLoops)
        {
            return Task.FromResult(HealthCheckResult.Healthy("loops disabled by configuration"));
        }

        var stale = new List<string>();
        foreach (var (name, interval) in new[] { (EscalationLoop.Name, _options.EscalationIntervalSeconds), (PagingLoop.Name, _options.PagingIntervalSeconds) })
        {
            var last = _heartbeat.Last(name);
            if (last is null || _clock.UtcNow - last.Value > TimeSpan.FromSeconds(interval * 3))
            {
                stale.Add(name);
            }
        }

        return Task.FromResult(stale.Count == 0 ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy($"stale loops: {string.Join(", ", stale)}"));
    }
}
