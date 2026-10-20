namespace Tamaneen.Infrastructure.Security;

/// <summary>Wraps and unwraps patient data keys with the platform key-encryption key (KEK).</summary>
public interface IKeyEncryptionKeyProvider
{
    /// <summary>Identifier of the KEK used for new wraps, stored next to each wrapped key.</summary>
    string CurrentKeyId { get; }

    Task<byte[]> WrapAsync(byte[] dataKey, CancellationToken cancellationToken);

    Task<byte[]> UnwrapAsync(byte[] wrappedKey, string keyId, CancellationToken cancellationToken);
}
