using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Tamaneen.Api.Auth;

public sealed class ServerSecretsOptions
{
    public const string Section = "Auth";

    /// <summary>Base64 of at least 32 bytes, from Key Vault. Purpose keys are derived from it with HKDF.</summary>
    [Required]
    public string ServerKeyBase64 { get; set; } = string.Empty;
}

/// <summary>
/// Server-side secrets that are not patient data: pending sign-up details, contacts kept only for a
/// resend, authenticator secrets and keyed hashes of one-time codes. Each use has its own derived key,
/// so a value protected for one purpose never decrypts under another.
/// </summary>
public sealed class ServerSecrets
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _root;

    public ServerSecrets(IOptions<ServerSecretsOptions> options)
    {
        var raw = Convert.TryFromBase64String(options.Value.ServerKeyBase64 ?? string.Empty, new byte[512], out var written) ? Convert.FromBase64String(options.Value.ServerKeyBase64!) : Array.Empty<byte>();
        if (written < 32)
        {
            throw new InvalidOperationException("Auth:ServerKeyBase64 must be at least 32 bytes of base64.");
        }

        _root = raw;
    }

    public byte[] Protect(string purpose, string plaintext)
    {
        var key = Derive("protect:" + purpose);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var output = new byte[NonceSize + TagSize + plain.Length];
        RandomNumberGenerator.Fill(output.AsSpan(0, NonceSize));
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(output.AsSpan(0, NonceSize), plain, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize), Encoding.ASCII.GetBytes(purpose));
        return output;
    }

    public string Unprotect(string purpose, byte[] protectedValue)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);
        if (protectedValue.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Protected value is too short.");
        }

        var key = Derive("protect:" + purpose);
        var plain = new byte[protectedValue.Length - NonceSize - TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(protectedValue.AsSpan(0, NonceSize), protectedValue.AsSpan(NonceSize + TagSize), protectedValue.AsSpan(NonceSize, TagSize), plain, Encoding.ASCII.GetBytes(purpose));
        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>Keyed hash (HMAC-SHA256) for values that are compared but never read back, e.g. one-time codes.</summary>
    public byte[] Mac(string purpose, string value) => HMACSHA256.HashData(Derive("mac:" + purpose), Encoding.UTF8.GetBytes(value));

    public static bool Equal(byte[] a, byte[] b) => CryptographicOperations.FixedTimeEquals(a, b);

    private byte[] Derive(string info) => HKDF.DeriveKey(HashAlgorithmName.SHA256, _root, 32, salt: Encoding.ASCII.GetBytes("tamaneen-server-v1"), info: Encoding.ASCII.GetBytes(info));
}
