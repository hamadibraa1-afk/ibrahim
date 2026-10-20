using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Tamaneen.AiGateway.Deidentification;

public sealed class PresidioOptions
{
    /// <summary>Base address of a Presidio Analyzer sidecar; empty disables the detector.</summary>
    public string AnalyzerUrl { get; set; } = string.Empty;

    public double ScoreThreshold { get; set; } = 0.5;

    public int TimeoutMs { get; set; } = 1500;
}

/// <summary>
/// Optional extra recall from Microsoft Presidio (NER for names and places the patterns miss). Runs inside
/// the cluster only; the text never leaves the private network on this hop.
/// </summary>
public sealed class PresidioPiiDetector(HttpClient http, IOptions<PresidioOptions> options) : IPiiDetector
{
    public async Task<IReadOnlyList<PiiSpan>> DetectAsync(string text, string language, KnownIdentifiers known, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.AnalyzerUrl))
        {
            return Array.Empty<PiiSpan>();
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.TimeoutMs);
        var request = new AnalyzeRequest(text, language, settings.ScoreThreshold);
        using var response = await http.PostAsJsonAsync(new Uri(new Uri(settings.AnalyzerUrl), "analyze"), request, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var results = await response.Content.ReadFromJsonAsync<List<AnalyzeResult>>(timeout.Token).ConfigureAwait(false) ?? [];
        var utf16 = CodePointToUtf16(text);
        var spans = new List<PiiSpan>(results.Count);
        foreach (var r in results)
        {
            if (Map(r.EntityType) is not { } type || r.Start < 0 || r.End > utf16.Length - 1 || r.End <= r.Start)
            {
                continue;
            }

            var start = utf16[r.Start];
            spans.Add(new PiiSpan(start, utf16[r.End] - start, type, r.Score));
        }

        return spans;
    }

    /// <summary>Presidio reports Python code-point offsets; .NET strings index UTF-16 units (emoji differ).</summary>
    internal static int[] CodePointToUtf16(string text)
    {
        var map = new List<int>(text.Length + 1);
        for (var i = 0; i < text.Length; i++)
        {
            map.Add(i);
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
        }

        map.Add(text.Length);
        return map.ToArray();
    }

    private static PiiType? Map(string? entity) => entity?.ToUpper(CultureInfo.InvariantCulture) switch
    {
        "PERSON" => PiiType.Person,
        "PHONE_NUMBER" => PiiType.Phone,
        "EMAIL_ADDRESS" => PiiType.Email,
        "IBAN_CODE" => PiiType.Iban,
        "CREDIT_CARD" => PiiType.CreditCard,
        "URL" => PiiType.Url,
        "IP_ADDRESS" => PiiType.IpAddress,
        "LOCATION" => PiiType.Address,
        // DATE_TIME is left out on purpose: it fires on "tonight" and "tomorrow", which the model needs.
        "MEDICAL_LICENSE" or "US_SSN" or "UK_NHS" or "US_PASSPORT" or "US_DRIVER_LICENSE" => PiiType.NationalId,
        _ => null,
    };

    private sealed record AnalyzeRequest(
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("language")] string Language,
        [property: JsonPropertyName("score_threshold")] double ScoreThreshold);

    private sealed record AnalyzeResult(
        [property: JsonPropertyName("entity_type")] string? EntityType,
        [property: JsonPropertyName("start")] int Start,
        [property: JsonPropertyName("end")] int End,
        [property: JsonPropertyName("score")] double Score);
}
