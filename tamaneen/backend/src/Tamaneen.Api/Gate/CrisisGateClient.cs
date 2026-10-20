using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Gate;

public sealed class CrisisGateOptions
{
    public const string Section = "CrisisGate";

    /// <summary>Base address of Tamaneen.Crisis.Worker inside the private network.</summary>
    [Required]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Same value as Crisis:Worker:ServiceKeyBase64 on the worker (Key Vault).</summary>
    [Required]
    public string ServiceKeyBase64 { get; set; } = string.Empty;

    [Range(1, 30)]
    public int TimeoutSeconds { get; set; } = 8;
}

public sealed record GateAnswers(string Instrument, IReadOnlyDictionary<int, int> Scores);

/// <summary>What the gate decided. <see cref="Unavailable"/> means the safety check could not run.</summary>
public sealed record GateVerdict(bool Crisis, RiskLevel Level, string? Script, Guid? CrisisEventId, IReadOnlyList<string> RuleIds, bool Unavailable)
{
    public static GateVerdict ServiceUnavailable { get; } = new(false, RiskLevel.None, null, null, [], true);
}

/// <summary>
/// Every patient text and questionnaire answer passes through the crisis worker before the API stores it,
/// sends it to the AI or shows anything else. If the worker cannot be reached the content is not processed:
/// callers turn <see cref="GateVerdict.Unavailable"/> into a 503 that tells the patient where to get help.
/// </summary>
public sealed partial class CrisisGateClient(HttpClient http, IOptions<CrisisGateOptions> options, ILogger<CrisisGateClient> logger)
{
    public const string ServiceKeyHeader = "X-Tamaneen-Service-Key";

    public async Task<GateVerdict> EvaluateAsync(Guid patientId, string language, string? text, GateAnswers? answers, string source, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(options.Value.BaseUrl), "internal/v1/gate/evaluate"))
        {
            Content = JsonContent.Create(new
            {
                patientId,
                language,
                text,
                answers = answers is null ? null : new { instrument = answers.Instrument, scores = answers.Scores },
                source,
            }),
        };
        request.Headers.Add(ServiceKeyHeader, options.Value.ServiceKeyBase64);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogGateStatus(logger, (int)response.StatusCode, source);
                return GateVerdict.ServiceUnavailable;
            }

            var body = await response.Content.ReadFromJsonAsync<EvaluateResponse>(timeout.Token).ConfigureAwait(false);
            if (body is null)
            {
                return GateVerdict.ServiceUnavailable;
            }

            return new GateVerdict(body.Decision == "blocked", (RiskLevel)body.Level, body.Script, body.CrisisEventId, body.RuleIds ?? [], false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !cancellationToken.IsCancellationRequested)
        {
            LogGateUnreachable(logger, source, ex.GetType().Name);
            return GateVerdict.ServiceUnavailable;
        }
    }

    private sealed record EvaluateResponse(string Decision, int Level, Guid? CrisisEventId, string? Script, bool ChatLocked, List<string>? RuleIds);

    [LoggerMessage(EventId = 4101, Level = LogLevel.Error, Message = "Crisis gate returned {Status} for {Source}; content was not processed.")]
    private static partial void LogGateStatus(ILogger logger, int status, string source);

    [LoggerMessage(EventId = 4102, Level = LogLevel.Critical, Message = "Crisis gate unreachable for {Source} ({Error}); content was not processed.")]
    private static partial void LogGateUnreachable(ILogger logger, string source, string error);
}
