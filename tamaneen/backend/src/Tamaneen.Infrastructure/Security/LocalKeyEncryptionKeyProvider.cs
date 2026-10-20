using System.Security.Cryptography;

namespace Tamaneen.Infrastructure.Security;

/// <summary>
/// AES-256-GCM key wrapping with a KEK from configuration. Wrapped format: nonce(12) | tag(16) | ciphertext(32).
/// Registration refuses this provider in the Production environment.
/// </summary>
public sealed class LocalKeyEncryptionKeyProvider : IKeyEncryptionKeyProvider
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _kek;

    public LocalKeyEncryptionKeyProvider(byte[] kek)
    {
        if (kek is not { Length: 32 })
        {
            throw new ArgumentException("The local key-encryption key must be exactly 32 bytes.", nameof(kek));
        }

        _kek = kek;
        CurrentKeyId = "local:" + Convert.ToHexString(SHA256.HashData(kek))[..16].ToLowerInvariant();
    }

    public string CurrentKeyId { get; }

    public Task<byte[]> WrapAsync(byte[] dataKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataKey);
        var output = new byte[NonceSize + TagSize + dataKey.Length];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_kek, TagSize);
        aes.Encrypt(nonce, dataKey, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize), KeyIdBytes());
        return Task.FromResult(output);
    }

    public Task<byte[]> UnwrapAsync(byte[] wrappedKey, string keyId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wrappedKey);
        if (!string.Equals(keyId, CurrentKeyId, StringComparison.Ordinal))
        {
            throw new CryptographicException($"Data key was wrapped with '{keyId}', not the configured local key.");
        }

        if (wrappedKey.Length <= NonceSize + TagSize)
        {
            throw new CryptographicException("Wrapped key is too short.");
        }

        var plain = new byte[wrappedKey.Length - NonceSize - TagSize];
        using var aes = new AesGcm(_kek, TagSize);
        aes.Decrypt(wrappedKey.AsSpan(0, NonceSize), wrappedKey.AsSpan(NonceSize + TagSize), wrappedKey.AsSpan(NonceSize, TagSize), plain, KeyIdBytes());
        return Task.FromResult(plain);
    }

    private byte[] KeyIdBytes() => System.Text.Encoding.ASCII.GetBytes(CurrentKeyId);
}
