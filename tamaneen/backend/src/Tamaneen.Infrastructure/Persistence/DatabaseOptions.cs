using System.ComponentModel.DataAnnotations;

namespace Tamaneen.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Turns on Always Encrypted in the driver and registers the Key Vault provider.</summary>
    public bool AlwaysEncrypted { get; set; }

    [Range(5, 300)]
    public int CommandTimeoutSeconds { get; set; } = 30;
}
