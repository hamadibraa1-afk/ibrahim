using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Tamaneen.AiGateway.Deidentification;
using Tamaneen.AiGateway.Model;
using Tamaneen.AiGateway.Prompts;
using Tamaneen.AiGateway.Validation;
using Tamaneen.SharedKernel;

namespace Tamaneen.AiGateway.Gateway;

/// <summary>
/// The proxy every AI feature goes through:
/// de-identify → prompt in the patient's language → model (timeout / retry / breaker) → validate
/// (→ one language repair) → put names back → record metadata. Any failure along the way yields the
/// localized fallback, never an exception or raw model text, and nothing identifying ever reaches the model.
/// </summary>
public sealed partial class AiGatewayService(
    PromptCatalog prompts,
    Deidentifier deidentifier,
    OutputValidator validator,
    ILanguageModel model,
    AiResiliencePipeline resilience,
    IAiCallLog callLog,
    IClock clock,
    IOptions<AiGatewayOptions> options,
    ILogger<AiGatewayService> logger) : IAiGateway
{
    public async Task<AiResponse> AskAsync(AiRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var template = prompts.Get(request.Purpose);
        var language = SupportedLanguages.Normalize(request.Language);
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > options.Value.MaxMessageChars)
        {
            throw new DomainException(ErrorCodes.AiMessageInvalid, "AI message is empty or too long.", options.Value.MaxMessageChars);
        }

        var record = AiCallRecord.Start(request.PseudonymId, template.Purpose, template.Version, model.ModelId, language, clock);
        var stopwatch = Stopwatch.StartNew();
        int inputTokens = 0, outputTokens = 0;

        AiResponse Finish(AiCallOutcome outcome, string? text)
        {
            record.Complete(outcome, (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds), inputTokens, outputTokens);
            return text is null
                ? new AiResponse(template.For(language).Fallback, language, true, outcome, template.Version)
                : new AiResponse(text, language, false, outcome, template.Version);
        }

        AiResponse response;
        using (var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            budget.CancelAfter(TimeSpan.FromSeconds(options.Value.TotalBudgetSeconds));
            try
            {
                response = await RunAsync(request, template, language, (i, o) => { inputTokens += i; outputTokens += o; }, Finish, budget.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                response = Finish(AiCallOutcome.TimedOut, null);
            }
        }

        try
        {
            await callLog.RecordAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The patient still gets the reply; the missing metadata row is visible in logs and alerts.
            LogRecordFailed(logger, record.Id, ex.GetType().Name);
        }

        return response;
    }

    private async Task<AiResponse> RunAsync(
        AiRequest request,
        PromptTemplate template,
        string language,
        Action<int, int> addUsage,
        Func<AiCallOutcome, string?, AiResponse> finish,
        CancellationToken cancellationToken)
    {
        var map = new PiiTokenMap();
        List<ChatTurn> messages;
        try
        {
            messages = await DeidentifyConversationAsync(request, template, language, map, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fail closed: if the text cannot be cleaned it is not sent.
            LogDeidentificationFailed(logger, template.Purpose, ex.GetType().Name);
            return finish(AiCallOutcome.DeidentificationFailed, null);
        }

        var system = template.SystemFor(language);
        var repaired = false;
        for (var attempt = 0; ; attempt++)
        {
            LanguageModelReply reply;
            try
            {
                reply = await resilience.Pipeline.ExecuteAsync(
                    static async (state, ct) => await state.model.CompleteAsync(state.req, ct).ConfigureAwait(false),
                    (model, req: new LanguageModelRequest(system, messages, template.MaxTokens, template.Effort)),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (BrokenCircuitException)
            {
                return finish(AiCallOutcome.CircuitOpen, null);
            }
            catch (TimeoutRejectedException)
            {
                return finish(AiCallOutcome.TimedOut, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                LogModelFailed(logger, template.Purpose, ex.GetType().Name);
                return finish(AiCallOutcome.Failed, null);
            }

            addUsage(reply.InputTokens, reply.OutputTokens);
            if (reply.Stop == LanguageModelStop.Refused)
            {
                return finish(AiCallOutcome.Refused, null);
            }

            var validation = await validator.ValidateAsync(reply.Text, language, template.MaxReplyChars, reply.Stop == LanguageModelStop.MaxTokens, map, cancellationToken).ConfigureAwait(false);
            if (validation.Passed)
            {
                return finish(repaired ? AiCallOutcome.LanguageRepaired : AiCallOutcome.Succeeded, map.Rehydrate(reply.Text, language));
            }

            LogRejected(logger, template.Purpose, language, string.Join(',', validation.Violations));
            if (attempt == 0 && validation.OnlyLanguage)
            {
                // One rewrite in the right language; the reply stays de-identified in the conversation.
                repaired = true;
                messages = [.. messages, new ChatTurn(ChatRole.Assistant, reply.Text), new ChatTurn(ChatRole.User, template.For(language).Repair)];
                continue;
            }

            return finish(AiCallOutcome.ValidatorRejected, null);
        }
    }

    private async Task<List<ChatTurn>> DeidentifyConversationAsync(AiRequest request, PromptTemplate template, string language, PiiTokenMap map, CancellationToken cancellationToken)
    {
        // Earlier turns are capped like the new message, keeping their most recent part.
        var max = options.Value.MaxMessageChars;
        var history = template.HistoryTurns == 0
            ? []
            : request.History.TakeLast(template.HistoryTurns).Select(t => t.Text.Length > max ? t with { Text = t.Text[^max..] } : t).ToList();

        // The API requires the conversation to start with the user and to alternate; drop a leading reply.
        while (history.Count > 0 && history[0].Role != ChatRole.User)
        {
            history.RemoveAt(0);
        }

        var messages = new List<ChatTurn>(history.Count + 1);
        foreach (var turn in history.Append(new ChatTurn(ChatRole.User, request.Message)))
        {
            var clean = await deidentifier.DeidentifyAsync(turn.Text, language, request.Known, map, cancellationToken).ConfigureAwait(false);
            if (messages.Count > 0 && messages[^1].Role == turn.Role)
            {
                messages[^1] = messages[^1] with { Text = messages[^1].Text + "\n\n" + clean.Text };
            }
            else
            {
                messages.Add(new ChatTurn(turn.Role, clean.Text));
            }
        }

        return messages;
    }

    [LoggerMessage(EventId = 3111, Level = LogLevel.Error, Message = "De-identification failed for {Purpose} ({Error}); the text was not sent to the model.")]
    private static partial void LogDeidentificationFailed(ILogger logger, string purpose, string error);

    [LoggerMessage(EventId = 3112, Level = LogLevel.Warning, Message = "Model call for {Purpose} failed ({Error}); fallback reply served.")]
    private static partial void LogModelFailed(ILogger logger, string purpose, string error);

    [LoggerMessage(EventId = 3113, Level = LogLevel.Warning, Message = "Reply for {Purpose} in {Language} rejected: {Violations}.")]
    private static partial void LogRejected(ILogger logger, string purpose, string language, string violations);

    [LoggerMessage(EventId = 3114, Level = LogLevel.Error, Message = "Could not store AI call record {RecordId} ({Error}).")]
    private static partial void LogRecordFailed(ILogger logger, Guid recordId, string error);
}
