namespace Tamaneen.AiGateway.Model;

/// <summary>
/// Stands in when no API key is configured (local development, CI). Every call fails, so the gateway serves
/// its localized fallback; production hosts refuse to start without a key instead.
/// </summary>
public sealed class NotConfiguredLanguageModel(string modelId) : ILanguageModel
{
    public string ModelId { get; } = modelId;

    public Task<LanguageModelReply> CompleteAsync(LanguageModelRequest request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("No language model is configured (Anthropic:ApiKey is empty).");
}
