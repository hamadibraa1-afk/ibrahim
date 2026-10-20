using System.Text;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;

namespace Tamaneen.AiGateway.Model;

public sealed class AnthropicOptions
{
    /// <summary>Read from configuration / Key Vault (Anthropic__ApiKey); never committed.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "claude-opus-5-5";

    public string? BaseUrl { get; set; }
}

/// <summary>Claude through the official Anthropic SDK. Receives de-identified text only.</summary>
public sealed class AnthropicLanguageModel : ILanguageModel, IDisposable
{
    private readonly AnthropicClient client;

    public AnthropicLanguageModel(IOptions<AnthropicOptions> options)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("Anthropic:ApiKey is not configured.");
        }

        ModelId = settings.Model;
        // Retries and the overall deadline belong to the gateway's resilience pipeline.
        client = string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? new AnthropicClient { ApiKey = settings.ApiKey, MaxRetries = 0 }
            : new AnthropicClient { ApiKey = settings.ApiKey, MaxRetries = 0, BaseUrl = settings.BaseUrl };
    }

    public string ModelId { get; }

    public async Task<LanguageModelReply> CompleteAsync(LanguageModelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var parameters = new MessageCreateParams
        {
            Model = ModelId,
            MaxTokens = request.MaxTokens,
            System = request.System,
            OutputConfig = new OutputConfig { Effort = ToEffort(request.Effort) },
            Messages = request.Messages.Select(m => new MessageParam
            {
                Role = m.Role == ChatRole.User ? Role.User : Role.Assistant,
                Content = m.Text,
            }).ToList(),
        };

        Message response;
        try
        {
            response = await client.Messages.Create(parameters, cancellationToken).ConfigureAwait(false);
        }
        catch (AnthropicRateLimitException ex)
        {
            throw new TransientModelException("Rate limited.", ex);
        }
        catch (Anthropic5xxException ex)
        {
            throw new TransientModelException("Model service error.", ex);
        }
        catch (AnthropicIOException ex)
        {
            throw new TransientModelException("Network error.", ex);
        }

        var text = new StringBuilder();
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var t))
            {
                text.Append(t.Text);
            }
        }

        var stop = (response.StopReason is { } reason ? (string)reason : null) switch
        {
            "end_turn" or "stop_sequence" => LanguageModelStop.Completed,
            "max_tokens" => LanguageModelStop.MaxTokens,
            "refusal" => LanguageModelStop.Refused,
            _ => LanguageModelStop.Other,
        };
        return new LanguageModelReply(text.ToString().Trim(), stop, response.Model, (int)response.Usage.InputTokens, (int)response.Usage.OutputTokens);
    }

    public void Dispose() => client.Dispose();

    private static Effort ToEffort(string effort) => effort switch
    {
        "low" => Effort.Low,
        "high" => Effort.High,
        _ => Effort.Medium,
    };
}
