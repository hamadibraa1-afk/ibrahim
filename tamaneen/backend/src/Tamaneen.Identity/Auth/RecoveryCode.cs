using Tamaneen.SharedKernel;

namespace Tamaneen.Identity.Auth;

/// <summary>One-time account recovery code. Only its SHA-256 hash is stored.</summary>
public sealed class RecoveryCode : Entity
{
    public const int SetSize = 10;

    private RecoveryCode(Guid id, Guid userId, byte[] codeHash, DateTimeOffset createdAtUtc)
        : base(id)
    {
        UserId = userId;
        CodeHash = codeHash;
        CreatedAtUtc = createdAtUtc;
    }

    // EF Core
    private RecoveryCode()
    {
        CodeHash = Array.Empty<byte>();
    }

    public Guid UserId { get; private set; }

    public byte[] CodeHash { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? UsedAtUtc { get; private set; }

    public static RecoveryCode Create(Guid userId, byte[] codeHash, IClock clock) => new(Guid.NewGuid(), userId, codeHash, clock.UtcNow);

    public void MarkUsed(IClock clock) => UsedAtUtc = clock.UtcNow;
}
