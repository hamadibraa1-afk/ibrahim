using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using Tamaneen.AiGateway.Model;

namespace Tamaneen.AiGateway.Gateway;

/// <summary>
/// Singleton so the circuit breaker sees every call in the process. Order, outermost first: one jittered retry
/// for transient failures, the circuit breaker, then a per-attempt timeout. When the model is unhealthy the
/// breaker opens and patients get the fallback immediately instead of waiting out a timeout each.
/// </summary>
public sealed class AiResiliencePipeline
{
    public AiResiliencePipeline(IOptions<AiGatewayOptions> options, TimeProvider timeProvider)
    {
        var o = options.Value;
        var builder = new ResiliencePipelineBuilder<LanguageModelReply> { TimeProvider = timeProvider };
        if (o.MaxRetries > 0)
        {
            builder.AddRetry(new RetryStrategyOptions<LanguageModelReply>
            {
                MaxRetryAttempts = o.MaxRetries,
                Delay = TimeSpan.FromMilliseconds(o.RetryDelayMs),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<LanguageModelReply>().Handle<TransientModelException>(),
            });
        }

        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<LanguageModelReply>
        {
            FailureRatio = o.BreakerFailureRatio,
            MinimumThroughput = o.BreakerMinimumThroughput,
            SamplingDuration = TimeSpan.FromSeconds(o.BreakerSamplingSeconds),
            BreakDuration = TimeSpan.FromSeconds(o.BreakerBreakSeconds),
            ShouldHandle = new PredicateBuilder<LanguageModelReply>().Handle<TransientModelException>().Handle<TimeoutRejectedException>(),
        });
        builder.AddTimeout(TimeSpan.FromSeconds(o.TimeoutSeconds));
        Pipeline = builder.Build();
    }

    public ResiliencePipeline<LanguageModelReply> Pipeline { get; }
}
