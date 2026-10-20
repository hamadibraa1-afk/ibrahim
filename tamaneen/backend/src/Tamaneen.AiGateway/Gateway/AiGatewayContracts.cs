using System.ComponentModel.DataAnnotations;
using Tamaneen.AiGateway.Deidentification;
using Tamaneen.AiGateway.Model;

namespace Tamaneen.AiGateway.Gateway;

/// <summary>
/// One request to the companion. <see cref="History"/> holds earlier turns in plain text (as the patient
/// saw them); the gateway de-identifies everything again on each call, so nothing identifying is cached.
/// </summary>
public sealed record AiRequest(
    string PseudonymId,
    string Purpose,
    string Language,
    string Message,
    IReadOnlyList<ChatTurn> History,
    KnownIdentifiers Known);

/// <summary>The reply shown to the patient: either the validated model reply or the localized fallback.</summary>
public sealed record AiResponse(string Text, string Language, bool IsFallback, AiCallOutcome Outcome, string PromptVersion);

public interface IAiGateway
{
    Task<AiResponse> AskAsync(AiRequest request, CancellationToken cancellationToken);
}

/// <summary>Persists call metadata (never content). Implemented in Infrastructure.</summary>
public interface IAiCallLog
{
    Task RecordAsync(AiCallRecord record, CancellationToken cancellationToken);
}

public sealed class AiGatewayOptions
{
    public const string Section = "AiGateway";

    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>Deadline for the whole call, across the retry and the language rewrite.</summary>
    [Range(1, 300)]
    public int TotalBudgetSeconds { get; set; } = 35;

    [Range(0, 3)]
    public int MaxRetries { get; set; } = 1;

    [Range(0, 10_000)]
    public int RetryDelayMs { get; set; } = 400;

    [Range(100, 16_000)]
    public int MaxMessageChars { get; set; } = 4000;

    [Range(0.05, 1.0)]
    public double BreakerFailureRatio { get; set; } = 0.5;

    [Range(2, 1000)]
    public int BreakerMinimumThroughput { get; set; } = 6;

    [Range(1, 3600)]
    public int BreakerSamplingSeconds { get; set; } = 60;

    [Range(1, 3600)]
    public int BreakerBreakSeconds { get; set; } = 30;
}
