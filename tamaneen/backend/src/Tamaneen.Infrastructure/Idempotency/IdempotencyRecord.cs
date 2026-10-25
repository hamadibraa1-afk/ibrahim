using Tamaneen.SharedKernel;

namespace Tamaneen.Infrastructure.Idempotency;

/// <summary>
/// One patient write sent with an <c>Idempotency-Key</c> (EC-01, EC-07). The patient app's outbox (P66) keeps the
/// key while a write waits offline and on every retry, so a resend after a lost response is answered with the
/// first result instead of saving the entry twice. The key is reserved before the write runs; the response is
/// stored once it succeeded, encrypted with the patient's own key because it can hold their words.
/// </summary>
public sealed class IdempotencyRecord : Entity
{
    private IdempotencyRecord(Guid id, Guid patientId, Guid key, string method, string path, DateTimeOffset reservedAtUtc)
        : base(id)
    {
        PatientId = patientId;
        Key = key;
        Method = method;
        Path = path;
        ReservedAtUtc = reservedAtUtc;
    }

    // EF Core
    private IdempotencyRecord()
    {
        Method = string.Empty;
        Path = string.Empty;
    }

    public Guid PatientId { get; private set; }

    /// <summary>The device-generated key. Unique per patient.</summary>
    public Guid Key { get; private set; }

    /// <summary>The request the key was first used for. The same key on another request is refused.</summary>
    public string Method { get; private set; }

    public string Path { get; private set; }

    /// <summary>When the request holding the key started. Refreshed when an abandoned reservation is taken over.</summary>
    public DateTimeOffset ReservedAtUtc { get; private set; }

    /// <summary>Set once the first request succeeded; until then the key is in progress.</summary>
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public int? StatusCode { get; private set; }

    /// <summary>The first response body (JSON), encrypted with the patient's key. Null for an empty response.</summary>
    public byte[]? ResponseCipher { get; private set; }

    public bool IsCompleted => CompletedAtUtc is not null;

    public static IdempotencyRecord Reserve(Guid patientId, Guid key, string method, string path, IClock clock)
    {
        if (key == Guid.Empty)
        {
            throw new ArgumentException("Idempotency key must not be empty.", nameof(key));
        }

        ArgumentException.ThrowIfNullOrEmpty(method);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(clock);
        return new IdempotencyRecord(Guid.NewGuid(), patientId, key, method, path, clock.UtcNow);
    }
}
