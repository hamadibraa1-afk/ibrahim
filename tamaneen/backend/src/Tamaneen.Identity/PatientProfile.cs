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

    /// <summary>The sign-in phone or e-mail, encrypted, for sign-in codes and the crisis call.</summary>
    public byte[]? ContactCipher { get; private set; }

    /// <summary>"phone" or "email": which kind of contact <see cref="ContactCipher"/> holds.</summary>
    public string ContactType { get; private set; } = "phone";

    public bool ShareLocation { get; private set; }

    public DateTimeOffset? ShareLocationSinceUtc { get; private set; }

    public DateTimeOffset? ConsentsCompletedAtUtc { get; private set; }

    /// <summary>Set when the safety-info step (P21) was saved or skipped.</summary>
    public DateTimeOffset? SafetyInfoCompletedAtUtc { get; private set; }

    /// <summary>"intake" or "direct" (P22); null until chosen.</summary>
    public string? CarePath { get; private set; }

    public string TimeZone { get; private set; } = "Asia/Riyadh";

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

    public void SetContact(byte[] contactCipher, string contactType)
    {
        EnsureNotShredded();
        ContactCipher = contactCipher;
        ContactType = contactType == "email" ? "email" : "phone";
    }

    public void SetDisplayName(byte[] cipher)
    {
        EnsureNotShredded();
        DisplayNameCipher = cipher;
    }

    /// <summary>Emergency contact data (P21, P34) as one encrypted JSON value; null removes it.</summary>
    public void SetSafetyInfo(byte[]? emergencyContactCipher, bool shareLocation, IClock clock)
    {
        EnsureNotShredded();
        EmergencyContactCipher = emergencyContactCipher;
        if (shareLocation && !ShareLocation)
        {
            ShareLocationSinceUtc = clock.UtcNow;
        }
        else if (!shareLocation)
        {
            ShareLocationSinceUtc = null;
        }

        ShareLocation = shareLocation;
        SafetyInfoCompletedAtUtc ??= clock.UtcNow;
    }

    public void CompleteConsents(IClock clock) => ConsentsCompletedAtUtc ??= clock.UtcNow;

    public void ChooseCarePath(string path)
    {
        if (path is not ("intake" or "direct"))
        {
            throw new DomainException(ErrorCodes.InvalidCarePath, $"Unknown care path '{path}'.", path);
        }

        CarePath = path;
    }

    public void SetTimeZone(string timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone) || timeZone.Length > 64 || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out _))
        {
            throw new DomainException(ErrorCodes.InvalidTimeZone, $"Unknown time zone '{timeZone}'.", timeZone ?? string.Empty);
        }

        TimeZone = timeZone;
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
        ContactCipher = null;
        ShareLocation = false;
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
