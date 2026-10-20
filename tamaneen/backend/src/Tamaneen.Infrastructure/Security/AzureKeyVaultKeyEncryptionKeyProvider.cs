using Azure.Core;
using Azure.Security.KeyVault.Keys.Cryptography;

namespace Tamaneen.Infrastructure.Security;

/// <summary>Wraps patient data keys with an RSA key held in Azure Key Vault Managed HSM (RSA-OAEP-256).</summary>
public sealed class AzureKeyVaultKeyEncryptionKeyProvider : IKeyEncryptionKeyProvider
{
    private readonly TokenCredential _credential;
    private readonly CryptographyClient _current;

    public AzureKeyVaultKeyEncryptionKeyProvider(Uri keyId, TokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(keyId);
        _credential = credential ?? throw new ArgumentNullException(nameof(credential));
        _current = new CryptographyClient(keyId, credential);
        CurrentKeyId = keyId.ToString();
    }

    public string CurrentKeyId { get; }

    public async Task<byte[]> WrapAsync(byte[] dataKey, CancellationToken cancellationToken)
    {
        var result = await _current.WrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, dataKey, cancellationToken).ConfigureAwait(false);
        return result.EncryptedKey;
    }

    public async Task<byte[]> UnwrapAsync(byte[] wrappedKey, string keyId, CancellationToken cancellationToken)
    {
        // Keys wrapped under an older KEK version are unwrapped with that version; rotation re-wraps lazily.
        var client = string.Equals(keyId, CurrentKeyId, StringComparison.Ordinal)
            ? _current
            : new CryptographyClient(new Uri(keyId), _credential);
        var result = await client.UnwrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, wrappedKey, cancellationToken).ConfigureAwait(false);
        return result.Key;
    }
}
