namespace Tamaneen.Infrastructure.Security;

/// <summary>
/// A patient's data-encryption key, stored only in wrapped form. Shredding erases <see cref="WrappedKey"/>;
/// after that no ciphertext of this patient can be decrypted, including in database backups.
/// </summary>
public sealed class PatientKey
{
    public PatientKey(Guid patientId, byte[] wrappedKey, string keyEncryptionKeyId, DateTimeOffset createdAtUtc)
    {
        PatientId = patientId;
        WrappedKey = wrappedKey;
        KeyEncryptionKeyId = keyEncryptionKeyId;
        CreatedAtUtc = createdAtUtc;
    }

    // EF Core
    private PatientKey()
    {
        KeyEncryptionKeyId = string.Empty;
    }

    public Guid PatientId { get; private set; }

    public byte[]? WrappedKey { get; private set; }

    public string KeyEncryptionKeyId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? ShreddedAtUtc { get; private set; }

    public bool IsShredded => ShreddedAtUtc is not null;

    public void Shred(DateTimeOffset nowUtc)
    {
        WrappedKey = null;
        ShreddedAtUtc ??= nowUtc;
    }
}
