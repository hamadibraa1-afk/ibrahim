using System.Security.Cryptography;
using System.Text;

namespace Tamaneen.Infrastructure.Security;

/// <summary>
/// Field-level encryption with the patient's own data key (AES-256-GCM).
/// Ciphertext format: version(1) | nonce(12) | tag(16) | ciphertext.
/// The associated data binds each value to its patient and field, so a value copied to another
/// patient's row or another column fails authentication instead of decrypting.
/// </summary>
public sealed class PatientDataProtector : IPatientDataProtector
{
    private const byte Version = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 1 + NonceSize + TagSize;
    private readonly IPatientKeyStore _keys;

    public PatientDataProtector(IPatientKeyStore keys) => _keys = keys;

    public async Task<byte[]> EncryptAsync(Guid patientId, string field, string plaintext, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(plaintext);
        var key = await _keys.GetOrCreateDataKeyAsync(patientId, cancellationToken).ConfigureAwait(false);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var output = new byte[HeaderSize + plain.Length];
        output[0] = Version;
        RandomNumberGenerator.Fill(output.AsSpan(1, NonceSize));
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(output.AsSpan(1, NonceSize), plain, output.AsSpan(HeaderSize), output.AsSpan(1 + NonceSize, TagSize), AssociatedData(patientId, field));
        CryptographicOperations.ZeroMemory(plain);
        return output;
    }

    public async Task<string> DecryptAsync(Guid patientId, string field, byte[] ciphertext, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(ciphertext);
        if (ciphertext.Length < HeaderSize || ciphertext[0] != Version)
        {
            throw new CryptographicException("Unsupported ciphertext format.");
        }

        var key = await _keys.GetDataKeyAsync(patientId, cancellationToken).ConfigureAwait(false);
        var plain = new byte[ciphertext.Length - HeaderSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(ciphertext.AsSpan(1, NonceSize), ciphertext.AsSpan(HeaderSize), ciphertext.AsSpan(1 + NonceSize, TagSize), plain, AssociatedData(patientId, field));
        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] AssociatedData(Guid patientId, string field) =>
        Encoding.ASCII.GetBytes($"tamaneen:v{Version}:{patientId:N}:{field}");
}

public interface IPatientDataProtector
{
    Task<byte[]> EncryptAsync(Guid patientId, string field, string plaintext, CancellationToken cancellationToken);

    Task<string> DecryptAsync(Guid patientId, string field, byte[] ciphertext, CancellationToken cancellationToken);
}

/// <summary>Field names used as associated data. Changing one makes existing values undecryptable.</summary>
public static class ProtectedFields
{
    public const string DisplayName = "identity.patient.display_name";
    public const string DateOfBirth = "identity.patient.date_of_birth";
    public const string EmergencyContact = "identity.patient.emergency_contact";
}
