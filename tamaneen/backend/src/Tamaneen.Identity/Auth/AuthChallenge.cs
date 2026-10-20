using Tamaneen.SharedKernel;

namespace Tamaneen.Identity.Auth;

public enum ChallengePurpose
{
    Signup = 0,
    Recover = 1,
    LoginSms = 2,

    /// <summary>A challenge that can never be completed: returned when the contact is unknown or already registered, so responses reveal nothing.</summary>
    Decoy = 3,
}

/// <summary>
/// A 6-digit code sent by SMS or e-mail. The code is stored as an HMAC. The contact and any pending signup
/// data are encrypted with the server key so a resend is possible. Everything is deleted once the challenge
/// is used or expires.
/// </summary>
public sealed class AuthChallenge : Entity
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    public const int MaxSends = 5;

    private AuthChallenge(Guid id, ChallengePurpose purpose, Guid? userId, byte[] codeHash, byte[] contactCipher, byte[]? payloadCipher, DateTimeOffset now)
        : base(id)
    {
        Purpose = purpose;
        UserId = userId;
        CodeHash = codeHash;
        ContactCipher = contactCipher;
        PayloadCipher = payloadCipher;
        CreatedAtUtc = now;
        ExpiresAtUtc = now + Lifetime;
        ResendAvailableAtUtc = now + ResendCooldown;
        Sends = 1;
    }

    // EF Core
    private AuthChallenge()
    {
        CodeHash = Array.Empty<byte>();
        ContactCipher = Array.Empty<byte>();
    }

    public ChallengePurpose Purpose { get; private set; }

    public Guid? UserId { get; private set; }

    public byte[] CodeHash { get; private set; }

    public byte[] ContactCipher { get; private set; }

    public byte[]? PayloadCipher { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset ResendAvailableAtUtc { get; private set; }

    public int Attempts { get; private set; }

    public int Sends { get; private set; }

    public DateTimeOffset? ConsumedAtUtc { get; private set; }

    public static AuthChallenge Create(ChallengePurpose purpose, Guid? userId, byte[] codeHash, byte[] contactCipher, byte[]? payloadCipher, IClock clock) =>
        new(Guid.NewGuid(), purpose, userId, codeHash, contactCipher, payloadCipher, clock.UtcNow);

    public bool IsUsable(IClock clock) => ConsumedAtUtc is null && ExpiresAtUtc > clock.UtcNow && Attempts < MaxAttempts;

    /// <summary>Counts an attempt. Returns false when the challenge can no longer be used.</summary>
    public bool RegisterAttempt(IClock clock)
    {
        if (!IsUsable(clock))
        {
            return false;
        }

        Attempts++;
        return true;
    }

    public void Consume(IClock clock)
    {
        ConsumedAtUtc = clock.UtcNow;
        PayloadCipher = null;
        ContactCipher = Array.Empty<byte>();
    }

    public TimeSpan ResendWait(IClock clock) => ResendAvailableAtUtc > clock.UtcNow ? ResendAvailableAtUtc - clock.UtcNow : TimeSpan.Zero;

    public void Resend(byte[] newCodeHash, IClock clock)
    {
        if (ConsumedAtUtc is not null || Sends >= MaxSends)
        {
            throw new DomainException(ErrorCodes.ChallengeExpired, $"Challenge {Id} cannot be resent.");
        }

        if (ResendWait(clock) > TimeSpan.Zero)
        {
            throw new DomainException(ErrorCodes.ResendTooSoon, "Resend requested during the cooldown.", (int)Math.Ceiling(ResendWait(clock).TotalSeconds));
        }

        CodeHash = newCodeHash;
        Sends++;
        Attempts = 0;
        ExpiresAtUtc = clock.UtcNow + Lifetime;
        ResendAvailableAtUtc = clock.UtcNow + ResendCooldown;
    }
}
