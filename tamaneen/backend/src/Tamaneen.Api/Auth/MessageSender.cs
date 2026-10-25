using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Tamaneen.Hosting;

namespace Tamaneen.Api.Auth;

public enum MessagePurpose
{
    SignupCode = 0,
    RecoverCode = 1,
    LoginCode = 2,

    /// <summary>Someone tried to sign up with a contact that already has an account.</summary>
    AlreadyRegistered = 3,

    /// <summary>A time that fits the patient's waitlist preferences opened (P56). Neutral text, no names.</summary>
    WaitlistSlotOpen = 4,
}

public sealed record OutgoingMessage(string Channel, string To, string Language, MessagePurpose Purpose, string Text);

/// <summary>Sends sign-in codes and account notices by SMS or e-mail through the messaging provider.</summary>
public interface IMessageSender
{
    Task SendAsync(OutgoingMessage message, CancellationToken cancellationToken);
}

/// <summary>Builds the localized text so every provider gets the same wording in the user's language.</summary>
public sealed class MessageComposer(IStringLocalizer<SharedResource> text)
{
    public OutgoingMessage Compose(string contactType, string contact, string language, MessagePurpose purpose, string? code) =>
        LocalizationSetup.WithLanguage(language, () => new OutgoingMessage(
            contactType == "email" ? "email" : "sms",
            contact,
            language,
            purpose,
            purpose switch
            {
                MessagePurpose.SignupCode => text["message.code.signup", code ?? string.Empty].Value,
                MessagePurpose.RecoverCode => text["message.code.recover", code ?? string.Empty].Value,
                MessagePurpose.LoginCode => text["message.code.login", code ?? string.Empty].Value,
                MessagePurpose.WaitlistSlotOpen => text["message.waitlist.slot_open"].Value,
                _ => text["message.already_registered"].Value,
            }));
}

public sealed class MessagingOptions
{
    public const string Section = "Messaging";

    /// <summary>HTTPS endpoint of the SMS/e-mail provider bridge. Empty in development: messages go to the dev outbox.</summary>
    public string WebhookUrl { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 key (base64) for the X-Tamaneen-Signature header.</summary>
    public string SigningKeyBase64 { get; set; } = string.Empty;
}

/// <summary>Posts messages to the provider bridge, signed so the bridge can reject anything not from us.</summary>
public sealed class WebhookMessageSender(HttpClient http, IOptions<MessagingOptions> options) : IMessageSender
{
    public async Task SendAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var body = JsonSerializer.SerializeToUtf8Bytes(new { channel = message.Channel, to = message.To, language = message.Language, purpose = message.Purpose.ToString(), text = message.Text });
        using var request = new HttpRequestMessage(HttpMethod.Post, o.WebhookUrl) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new("application/json");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        byte[] signed = [.. Encoding.ASCII.GetBytes(timestamp + "."), .. body];
        var signature = Convert.ToHexString(HMACSHA256.HashData(Convert.FromBase64String(o.SigningKeyBase64), signed)).ToLowerInvariant();
        request.Headers.Add("X-Tamaneen-Timestamp", timestamp);
        request.Headers.Add("X-Tamaneen-Signature", "sha256=" + signature);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>
/// Development and test sender: keeps the last messages in memory so a developer (or the end-to-end
/// tests) can read the code from GET /dev/outbox. Never registered outside Development and Testing.
/// </summary>
public sealed class InMemoryMessageSender : IMessageSender
{
    private readonly ConcurrentQueue<OutgoingMessage> _messages = new();

    public IReadOnlyList<OutgoingMessage> Messages => _messages.ToArray();

    public Task SendAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        _messages.Enqueue(message);
        while (_messages.Count > 200 && _messages.TryDequeue(out _))
        {
        }

        return Task.CompletedTask;
    }

    public OutgoingMessage? LastTo(string to) => _messages.Reverse().FirstOrDefault(m => string.Equals(m.To, to, StringComparison.OrdinalIgnoreCase));
}
