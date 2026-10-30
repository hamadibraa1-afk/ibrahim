using Tamaneen.SharedKernel;

namespace Tamaneen.Identity.Auth;

/// <summary>
/// A signed-in device. Holds the hash of the current refresh token and of the one before it: presenting
/// the previous token again means it was copied, so the whole session is revoked.
/// </summary>
public sealed class AuthSession : Entity
{
    public static readonly TimeSpan PatientLifetime = TimeSpan.FromDays(30);
    /// <summary>Guide §security: refresh tokens last 8 hours for staff. Idle sign-out after 15 minutes is the portal's (S32).</summary>
    public static readonly TimeSpan StaffLifetime = TimeSpan.FromHours(8);

    private AuthSession(Guid id, Guid userId, string client, string deviceLabel, byte[] refreshHash, DateTimeOffset now, TimeSpan lifetime)
        : base(id)
    {
        UserId = userId;
        Client = client;
        DeviceLabel = deviceLabel;
        RefreshTokenHash = refreshHash;
        CreatedAtUtc = now;
        LastActiveAtUtc = now;
        ExpiresAtUtc = now + lifetime;
    }

    // EF Core
    private AuthSession()
    {
        Client = string.Empty;
        DeviceLabel = string.Empty;
        RefreshTokenHash = Array.Empty<byte>();
    }

    public Guid UserId { get; private set; }

    /// <summary>"patient" or "staff".</summary>
    public string Client { get; private set; }

    public string DeviceLabel { get; private set; }

    public byte[] RefreshTokenHash { get; private set; }

    public byte[]? PreviousRefreshTokenHash { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset LastActiveAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public bool IsActive(IClock clock) => RevokedAtUtc is null && ExpiresAtUtc > clock.UtcNow;

    public static AuthSession Start(Guid userId, string client, string deviceLabel, byte[] refreshHash, IClock clock)
    {
        var label = string.IsNullOrWhiteSpace(deviceLabel) ? "unknown" : deviceLabel.Trim();
        return new AuthSession(Guid.NewGuid(), userId, client, label.Length > 64 ? label[..64] : label, refreshHash, clock.UtcNow, client == "staff" ? StaffLifetime : PatientLifetime);
    }

    public void Rotate(byte[] newRefreshHash, IClock clock)
    {
        PreviousRefreshTokenHash = RefreshTokenHash;
        RefreshTokenHash = newRefreshHash;
        LastActiveAtUtc = clock.UtcNow;
    }

    public void Touch(IClock clock) => LastActiveAtUtc = clock.UtcNow;

    public void Revoke(IClock clock) => RevokedAtUtc ??= clock.UtcNow;
}
