using System.ComponentModel.DataAnnotations;

namespace Tamaneen.Api.Security;

public sealed class AuthOptions
{
    public const string Section = "Auth";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>OIDC authority for staff sign-in (production). When set, signing keys come from its metadata.</summary>
    public string? Authority { get; set; }

    /// <summary>Base64 of at least 32 bytes. HMAC-SHA256 key for tokens issued by the platform itself.</summary>
    public string? SigningKeyBase64 { get; set; }
}
