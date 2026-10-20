using Tamaneen.SharedKernel;

namespace Tamaneen.Identity;

public enum AccountStatus
{
    PendingVerification = 0,
    Active = 1,
    Locked = 2,
    Closed = 3,
}

/// <summary>
/// A sign-in identity for a patient or staff member. It holds no readable contact data:
/// email or phone is stored only as a keyed hash for lookup (see <see cref="IContactHasher"/>).
/// </summary>
public sealed class UserAccount : Entity
{
    private UserAccount(Guid id, string pseudonymId, string role, byte[] contactLookupHash, string preferredLanguage, DateTimeOffset createdAtUtc)
        : base(id)
    {
        PseudonymId = pseudonymId;
        Role = role;
        ContactLookupHash = contactLookupHash;
        PreferredLanguage = preferredLanguage;
        CreatedAtUtc = createdAtUtc;
        Status = AccountStatus.PendingVerification;
    }

    // EF Core
    private UserAccount()
    {
        PseudonymId = string.Empty;
        Role = string.Empty;
        ContactLookupHash = Array.Empty<byte>();
        PreferredLanguage = SupportedLanguages.Default;
    }

    public string PseudonymId { get; private set; }

    public string Role { get; private set; }

    /// <summary>HMAC-SHA256 of the normalized email or phone. 32 bytes. Unique.</summary>
    public byte[] ContactLookupHash { get; private set; }

    public string PreferredLanguage { get; private set; }

    public AccountStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static UserAccount Register(string role, byte[] contactLookupHash, string preferredLanguage, IClock clock)
    {
        if (!AppRoles.IsKnown(role) || AppRoles.IsService(role))
        {
            throw new ArgumentException($"Role '{role}' cannot be assigned to a user account.", nameof(role));
        }

        if (contactLookupHash is not { Length: 32 })
        {
            throw new ArgumentException("Contact lookup hash must be 32 bytes (HMAC-SHA256).", nameof(contactLookupHash));
        }

        if (!SupportedLanguages.IsSupported(preferredLanguage))
        {
            throw new DomainException(ErrorCodes.UnsupportedLanguage, $"Language '{preferredLanguage}' is not supported.", preferredLanguage);
        }

        return new UserAccount(
            Guid.NewGuid(),
            SharedKernel.PseudonymId.New().Value,
            role,
            contactLookupHash,
            SupportedLanguages.Normalize(preferredLanguage),
            clock.UtcNow);
    }

    public void Activate()
    {
        if (Status == AccountStatus.Closed)
        {
            throw new InvalidOperationException("A closed account cannot be activated.");
        }

        Status = AccountStatus.Active;
    }

    public void ChangeLanguage(string language)
    {
        if (!SupportedLanguages.IsSupported(language))
        {
            throw new DomainException(ErrorCodes.UnsupportedLanguage, $"Language '{language}' is not supported.", language);
        }

        PreferredLanguage = SupportedLanguages.Normalize(language);
    }

    public void Close() => Status = AccountStatus.Closed;
}
