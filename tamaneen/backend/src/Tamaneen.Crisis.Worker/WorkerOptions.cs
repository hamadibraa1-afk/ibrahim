using System.ComponentModel.DataAnnotations;

namespace Tamaneen.Crisis.Worker;

public sealed class WorkerOptions
{
    public const string Section = "Crisis:Worker";

    /// <summary>Identity recorded on audit rows written by the worker itself (escalations, paging).</summary>
    [Required]
    public Guid ServiceUserId { get; set; }

    /// <summary>Base64 of at least 32 bytes, shared with Tamaneen.Api through Key Vault. Protects the evaluate endpoint.</summary>
    [Required]
    public string ServiceKeyBase64 { get; set; } = string.Empty;

    [Range(5, 300)]
    public int EscalationIntervalSeconds { get; set; } = 15;

    [Range(2, 300)]
    public int PagingIntervalSeconds { get; set; } = 5;

    /// <summary>Off in tests that drive the loops by hand.</summary>
    public bool RunBackgroundLoops { get; set; } = true;

    /// <summary>Link included in pages so the clinician lands on the event in the crisis console (S04).</summary>
    [Required]
    [Url]
    public string ConsoleBaseUrl { get; set; } = string.Empty;
}

public sealed class PagingOptions
{
    public const string Section = "Crisis:Paging";

    /// <summary>Incident-paging webhook (second channel next to SMS and voice). Required, and HTTPS, in production.</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>Base64 of at least 32 bytes; signs every page so the receiver can verify it came from Tamaneen.</summary>
    public string? WebhookSecretBase64 { get; set; }

    [Range(1, 30)]
    public int TimeoutSeconds { get; set; } = 10;
}
