using System.ComponentModel.DataAnnotations;

namespace Tamaneen.Infrastructure.Security;

public enum KeyEncryptionProvider
{
    /// <summary>A key-encryption key from configuration. Development and automated tests only.</summary>
    Local = 0,

    /// <summary>An RSA key in Azure Key Vault Managed HSM. Required in production.</summary>
    AzureKeyVault = 1,
}

public sealed class EncryptionOptions
{
    public const string Section = "Encryption";

    public KeyEncryptionProvider Provider { get; set; } = KeyEncryptionProvider.Local;

    /// <summary>Base64 of 32 random bytes. Used only when <see cref="Provider"/> is Local.</summary>
    public string? LocalKeyBase64 { get; set; }

    /// <summary>Full key identifier, e.g. https://{hsm}.managedhsm.azure.net/keys/patient-kek/{version}.</summary>
    public string? KeyVaultKeyId { get; set; }

    /// <summary>How long an unwrapped patient data key may stay in memory.</summary>
    [Range(0, 3600)]
    public int DataKeyCacheSeconds { get; set; } = 300;
}

public sealed class PseudonymizationOptions
{
    public const string Section = "Pseudonymization";

    /// <summary>Base64 of at least 32 random bytes. The HMAC pepper for email and phone lookup hashes.</summary>
    [Required]
    public string ContactPepperBase64 { get; set; } = string.Empty;
}
