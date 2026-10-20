using Tamaneen.SharedKernel;

namespace Tamaneen.Identity;

/// <summary>
/// Personal data of a patient. Every identifying field is ciphertext produced with the patient's own
/// data key (envelope encryption). Destroying that key makes all of these fields unreadable at once
/// (crypto-shredding), including copies in backups.
/// </summary>
public sealed class PatientProfile
{
    private PatientProfile(Guid patientId, string pseudonymId, string cityCode, DateTimeOffset createdAtUtc)
    {
        PatientId = patientId;
        PseudonymId = pseudonymId;
        CityCode = cityCode;
        CreatedAtUtc = createdAtUtc;
    }

    // EF Core
    private PatientProfile()
    {
        PseudonymId = string.Empty;
        CityCode = string.Empty;
    }

    /// <summary>Same value as the patient's <see cref="UserAccount"/> id.</summary>
    public Guid PatientId { get; private set; }

    public string PseudonymId { get; private set; }

    public byte[]? DisplayNameCipher { get; private set; }

    public byte[]? DateOfBirthCipher { get; private set; }

    public byte[]? EmergencyContactCipher { get; private set; }

    /// <summary>City code inside the pilot region. Coarse enough not to identify a person.</summary>
    public string CityCode { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? ShreddedAtUtc { get; private set; }

    public bool IsShredded => ShreddedAtUtc is not null;

    public static PatientProfile Create(UserAccount account, string cityCode, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (account.Role != AppRoles.Patient)
        {
            throw new ArgumentException("A patient profile needs a patient account.", nameof(account));
        }

        if (string.IsNullOrWhiteSpace(cityCode) || cityCode.Length > 16)
        {
            throw new ArgumentException("City code is required and at most 16 characters.", nameof(cityCode));
        }

        return new PatientProfile(account.Id, account.PseudonymId, cityCode.Trim().ToUpperInvariant(), clock.UtcNow);
    }

    public void SetEncryptedFields(byte[]? displayName, byte[]? dateOfBirth, byte[]? emergencyContact)
    {
        EnsureNotShredded();
        DisplayNameCipher = displayName;
        DateOfBirthCipher = dateOfBirth;
        EmergencyContactCipher = emergencyContact;
    }

    /// <summary>Clears ciphertext after the data key is destroyed. The pseudonym stays for audit linkage.</summary>
    public void MarkShredded(IClock clock)
    {
        if (IsShredded)
        {
            return;
        }

        DisplayNameCipher = null;
        DateOfBirthCipher = null;
        EmergencyContactCipher = null;
        ShreddedAtUtc = clock.UtcNow;
    }

    private void EnsureNotShredded()
    {
        if (IsShredded)
        {
            throw new DomainException(ErrorCodes.PatientDataShredded, $"Patient {PseudonymId} has been shredded.");
        }
    }
}
