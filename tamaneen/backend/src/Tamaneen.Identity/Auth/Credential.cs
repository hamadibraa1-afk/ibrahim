using Tamaneen.SharedKernel;

namespace Tamaneen.Identity.Auth;

/// <summary>
/// Sign-in secrets of one account. The password is a PBKDF2 hash. The authenticator secret is encrypted
/// with a server key (staff have no patient data key). Five failures lock sign-in for 15 minutes.
/// </summary>
public sealed class Credential
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    private Credential(Guid userId, string passwordHash, DateTimeOffset now)
    {
        UserId = userId;
        PasswordHash = passwordHash;
        PasswordChangedAtUtc = now;
    }

    // EF Core
    private Credential()
    {
        PasswordHash = string.Empty;
    }

    public Guid UserId { get; private set; }

    public string PasswordHash { get; private set; }

    public DateTimeOffset PasswordChangedAtUtc { get; private set; }

    public int FailedAttempts { get; private set; }

    public DateTimeOffset? LockedUntilUtc { get; private set; }

    public byte[]? TotpSecretCipher { get; private set; }

    public bool TotpEnabled { get; private set; }

    /// <summary>Last accepted 30-second step, so the same code cannot be used twice.</summary>
    public long? TotpLastStep { get; private set; }

    public static Credential Create(Guid userId, string passwordHash, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        return new Credential(userId, passwordHash, clock.UtcNow);
    }

    public bool IsLocked(IClock clock) => LockedUntilUtc is { } until && until > clock.UtcNow;

    /// <summary>Counts a failed password and returns the attempts left before the lock (0 means now locked).</summary>
    public int RecordFailure(IClock clock)
    {
        if (LockedUntilUtc is { } until && until <= clock.UtcNow)
        {
            FailedAttempts = 0;
            LockedUntilUtc = null;
        }

        FailedAttempts++;
        if (FailedAttempts >= MaxFailures)
        {
            LockedUntilUtc = clock.UtcNow + LockDuration;
            return 0;
        }

        return MaxFailures - FailedAttempts;
    }

    public void RecordSuccess()
    {
        FailedAttempts = 0;
        LockedUntilUtc = null;
    }

    public void ChangePassword(string passwordHash, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
        PasswordChangedAtUtc = clock.UtcNow;
        RecordSuccess();
    }

    /// <summary>Re-hash after a successful sign-in when the hasher asks for it (parameters upgraded).</summary>
    public void UpgradeHash(string passwordHash) => PasswordHash = passwordHash;

    public void BeginTotpSetup(byte[] secretCipher)
    {
        if (TotpEnabled)
        {
            throw new DomainException(ErrorCodes.TotpAlreadyEnabled, "Authenticator already enabled.");
        }

        TotpSecretCipher = secretCipher;
    }

    public void EnableTotp(long step)
    {
        if (TotpSecretCipher is null)
        {
            throw new DomainException(ErrorCodes.TotpNotSetUp, "No authenticator secret to confirm.");
        }

        TotpEnabled = true;
        TotpLastStep = step;
    }

    /// <summary>
    /// Moves the authenticator to another device (S32): the new secret replaces the old one only once a code from
    /// the new device has been checked, so the old device keeps working until then.
    /// </summary>
    public void ReplaceTotp(byte[] secretCipher, long step)
    {
        ArgumentNullException.ThrowIfNull(secretCipher);
        TotpSecretCipher = secretCipher;
        TotpEnabled = true;
        TotpLastStep = step;
    }

    public void DisableTotp()
    {
        TotpEnabled = false;
        TotpSecretCipher = null;
        TotpLastStep = null;
    }

    /// <summary>Accepts a step only if it is newer than the last one used.</summary>
    public bool TryUseTotpStep(long step)
    {
        if (TotpLastStep is { } last && step <= last)
        {
            return false;
        }

        TotpLastStep = step;
        return true;
    }
}
