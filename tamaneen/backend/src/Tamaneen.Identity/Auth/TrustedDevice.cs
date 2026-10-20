using Tamaneen.SharedKernel;

namespace Tamaneen.Identity.Auth;

/// <summary>"Remember this device for 30 days": skips the second step for patients on this browser.</summary>
public sealed class TrustedDevice : Entity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    private TrustedDevice(Guid id, Guid userId, byte[] tokenHash, DateTimeOffset now)
        : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAtUtc = now + Lifetime;
    }

    // EF Core
    private TrustedDevice()
    {
        TokenHash = Array.Empty<byte>();
    }

    public Guid UserId { get; private set; }

    public byte[] TokenHash { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public static TrustedDevice Create(Guid userId, byte[] tokenHash, IClock clock) => new(Guid.NewGuid(), userId, tokenHash, clock.UtcNow);
}
