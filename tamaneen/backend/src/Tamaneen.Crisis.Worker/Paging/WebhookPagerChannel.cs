using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Tamaneen.Crisis.Worker.Paging;

/// <summary>A page as the receiving paging service sees it. No patient content.</summary>
public sealed record PageMessage(Guid PageId, Guid CrisisEventId, string PseudonymId, int Level, int Tier, string PatientLanguage, string ConsoleUrl, DateTimeOffset CreatedAtUtc);

public interface IPagerChannel
{
    Task SendAsync(PageMessage page, CancellationToken cancellationToken);
}

/// <summary>
/// Posts each page to the incident-paging service as signed JSON. Signature header:
/// <c>X-Tamaneen-Signature: sha256=hex(HMAC-SHA256(secret, timestamp + "." + body))</c>, with
/// <c>X-Tamaneen-Timestamp</c> in Unix seconds so receivers can reject replays. Pages are idempotent by PageId.
/// </summary>
public sealed class WebhookPagerChannel : IPagerChannel
{
    public const string SignatureHeader = "X-Tamaneen-Signature";
    public const string TimestampHeader = "X-Tamaneen-Timestamp";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly PagingOptions _options;

    public WebhookPagerChannel(HttpClient http, IOptions<PagingOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task SendAsync(PageMessage page, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookUrl) || string.IsNullOrWhiteSpace(_options.WebhookSecretBase64))
        {
            throw new InvalidOperationException("No paging channel is configured (Crisis:Paging).");
        }

        var body = JsonSerializer.Serialize(page, Json);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.WebhookUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(TimestampHeader, timestamp);
        request.Headers.Add(SignatureHeader, "sha256=" + Sign(Convert.FromBase64String(_options.WebhookSecretBase64), timestamp, body));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Paging service answered {(int)response.StatusCode}.");
        }
    }

    public static string Sign(byte[] secret, string timestamp, string body) =>
        Convert.ToHexString(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(timestamp + "." + body))).ToLowerInvariant();
}
