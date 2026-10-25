using System.ComponentModel.DataAnnotations;

namespace Tamaneen.Api.Sessions;

/// <summary>
/// <c>Video:*</c>. <c>Provider</c> is "dev" (default in Development and Testing: a local test room, no external
/// service, works offline) or "embed" (a provider that frames a room URL with a signed token). Production refuses "dev".
/// </summary>
public sealed class VideoOptions
{
    public const string Section = "Video";

    public string Provider { get; set; } = string.Empty;

    /// <summary>Longest life of a join token; it never outlives the appointment window. The apps ask again before it ends.</summary>
    [Range(1, 60)]
    public int TokenMinutes { get; set; } = 15;

    /// <summary>How often the apps send a presence heartbeat.</summary>
    [Range(5, 60)]
    public int HeartbeatSeconds { get; set; } = 15;

    /// <summary>P54: after this many minutes of a late therapist, the lobby offers the free reschedule and another therapist.</summary>
    [Range(1, 60)]
    public int LateOptionsAfterMinutes { get; set; } = 15;

    public EmbedVideoOptions Embed { get; set; } = new();
}

/// <summary>
/// <c>Video:Embed:*</c> for <see cref="EmbedVideoProvider"/>. <c>UrlTemplate</c> must be https and contain
/// <c>{room}</c> and <c>{token}</c>, e.g. <c>https://video.example/{room}?jwt={token}</c>. The token is an HS256 JWT
/// signed with <c>SigningKeyBase64</c> (shared with the provider, from Key Vault) for <c>Issuer</c> and <c>Audience</c>.
/// </summary>
public sealed class EmbedVideoOptions
{
    public string UrlTemplate { get; set; } = string.Empty;

    public string SigningKeyBase64 { get; set; } = string.Empty;

    public string Issuer { get; set; } = "tamaneen";

    public string Audience { get; set; } = "video";
}
