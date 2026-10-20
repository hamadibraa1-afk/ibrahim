namespace Tamaneen.AiGateway.Model;

public enum ChatRole
{
    User = 0,
    Assistant = 1,
}

public sealed record ChatTurn(ChatRole Role, string Text);

public sealed record LanguageModelRequest(string System, IReadOnlyList<ChatTurn> Messages, int MaxTokens, string Effort);

public enum LanguageModelStop
{
    Completed = 0,
    MaxTokens = 1,
    Refused = 2,
    Other = 3,
}

public sealed record LanguageModelReply(string Text, LanguageModelStop Stop, string ModelId, int InputTokens, int OutputTokens);

/// <summary>
/// The only seam through which the platform talks to a large language model. Implementations live in this
/// project and nowhere else (an architecture test enforces it), so every call passes through the gateway's
/// de-identification and output validation.
/// </summary>
public interface ILanguageModel
{
    string ModelId { get; }

    Task<LanguageModelReply> CompleteAsync(LanguageModelRequest request, CancellationToken cancellationToken);
}

/// <summary>A failure worth one retry (rate limit, overload, 5xx, network).</summary>
public sealed class TransientModelException : Exception
{
    public TransientModelException()
    {
    }

    public TransientModelException(string message)
        : base(message)
    {
    }

    public TransientModelException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
