using Tamaneen.SharedKernel;

namespace Tamaneen.AiGateway;

public enum AiCallOutcome
{
    Succeeded = 0,
    ValidatorRejected = 1,
    TimedOut = 2,
    Failed = 3,
    CircuitOpen = 4,
    Refused = 5,
    DeidentificationFailed = 6,
    LanguageRepaired = 7,
}

/// <summary>
/// Metadata of one language-model call. It never stores prompt or reply text: only who (by pseudonym),
/// which prompt and model versions, the language requested, cost and outcome.
/// </summary>
public sealed class AiCallRecord : Entity
{
    private AiCallRecord(Guid id, string pseudonymId, string purpose, string promptVersion, string modelId, string language, DateTimeOffset createdAtUtc)
        : base(id)
    {
        PseudonymId = pseudonymId;
        Purpose = purpose;
        PromptVersion = promptVersion;
        ModelId = modelId;
        Language = language;
        CreatedAtUtc = createdAtUtc;
    }

    // EF Core
    private AiCallRecord()
    {
        PseudonymId = string.Empty;
        Purpose = string.Empty;
        PromptVersion = string.Empty;
        ModelId = string.Empty;
        Language = SupportedLanguages.Default;
    }

    public string PseudonymId { get; private set; }

    public string Purpose { get; private set; }

    public string PromptVersion { get; private set; }

    public string ModelId { get; private set; }

    /// <summary>Language the model was instructed to answer in.</summary>
    public string Language { get; private set; }

    public AiCallOutcome Outcome { get; private set; }

    public int LatencyMs { get; private set; }

    public int InputTokens { get; private set; }

    public int OutputTokens { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static AiCallRecord Start(string pseudonymId, string purpose, string promptVersion, string modelId, string language, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        return new AiCallRecord(Guid.NewGuid(), SharedKernel.PseudonymId.Parse(pseudonymId).Value, purpose, promptVersion, modelId, SupportedLanguages.Normalize(language), clock.UtcNow);
    }

    public void Complete(AiCallOutcome outcome, int latencyMs, int inputTokens, int outputTokens)
    {
        if (latencyMs < 0 || inputTokens < 0 || outputTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(latencyMs), "Metrics cannot be negative.");
        }

        Outcome = outcome;
        LatencyMs = latencyMs;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
    }
}
