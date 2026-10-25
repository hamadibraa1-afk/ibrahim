using Tamaneen.SharedKernel;

namespace Tamaneen.Identity.Notifications;

/// <summary>What happened to the SMS or e-mail copy of a notification.</summary>
public enum ExternalDelivery
{
    /// <summary>No copy goes out: the person did not ask for one, or has no contact we can use (staff).</summary>
    None = 0,

    /// <summary>Waiting for the end of quiet hours, or for a retry after a failed send.</summary>
    Pending = 1,

    Sent = 2,

    /// <summary>Gave up after <see cref="UserNotification.MaxAttempts"/> tries.</summary>
    Failed = 3,

    /// <summary>Claimed by one dispatcher, so a second instance does not send it again.</summary>
    Sending = 4,
}

/// <summary>
/// One entry in a person's in-app inbox (P46, S41). It holds a kind and non-sensitive values (ids, times,
/// display names), never patient free text; the text is written from the catalog in the reader's language
/// when the inbox is read. Row-level security keeps every row to its recipient.
/// </summary>
public sealed class UserNotification : Entity
{
    public const int MaxAttempts = 5;
    public const int MaxDataLength = 2000;
    public const int MaxLinkLength = 300;

    private UserNotification(Guid id, Guid recipientUserId, string kind, string category, string dataJson, string? link, string? dedupeKey, DateTimeOffset now)
        : base(id)
    {
        RecipientUserId = recipientUserId;
        Kind = kind;
        Category = category;
        DataJson = dataJson;
        Link = link;
        DedupeKey = dedupeKey;
        CreatedAtUtc = now;
    }

    // EF Core
    private UserNotification()
    {
        Kind = string.Empty;
        Category = string.Empty;
        DataJson = "{}";
    }

    public Guid RecipientUserId { get; private set; }

    /// <summary>Dotted code, e.g. appointment.reminder_24h.</summary>
    public string Kind { get; private set; }

    /// <summary>risk, appointments, messages, homework, care, security or system (S41 filters).</summary>
    public string Category { get; private set; }

    public string DataJson { get; private set; }

    /// <summary>App-relative path the entry opens (patient app or staff portal, by recipient).</summary>
    public string? Link { get; private set; }

    /// <summary>Makes scheduled notifications (reminders, follow-ups) happen once per recipient, across restarts.</summary>
    public string? DedupeKey { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? ReadAtUtc { get; private set; }

    public ExternalDelivery External { get; private set; }

    /// <summary>sms or email, once a copy is due.</summary>
    public string? ExternalChannel { get; private set; }

    public DateTimeOffset? ExternalNotBeforeUtc { get; private set; }

    public byte ExternalAttempts { get; private set; }

    public DateTimeOffset? ExternalSentAtUtc { get; private set; }

    public bool IsRead => ReadAtUtc is not null;

    public static UserNotification Create(Guid recipientUserId, string kind, string category, string dataJson, string? link, string? dedupeKey, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (dataJson.Length > MaxDataLength)
        {
            throw new ArgumentException("Notification data too long.", nameof(dataJson));
        }

        if (link is { Length: > MaxLinkLength } || (link is not null && !link.StartsWith('/')))
        {
            link = null;
        }

        return new UserNotification(Guid.NewGuid(), recipientUserId, kind, category, dataJson, link, dedupeKey, clock.UtcNow);
    }

    public void MarkRead(IClock clock) => ReadAtUtc ??= clock.UtcNow;

    /// <summary>A copy goes out now or later (quiet hours, retry).</summary>
    public void QueueExternal(string channel, DateTimeOffset notBefore)
    {
        ExternalChannel = channel;
        External = ExternalDelivery.Pending;
        ExternalNotBeforeUtc = notBefore;
    }

    public void ExternalSent(IClock clock)
    {
        External = ExternalDelivery.Sent;
        ExternalSentAtUtc = clock.UtcNow;
        ExternalNotBeforeUtc = null;
    }

    /// <summary>Counts a failed try; waits a minute per attempt, then gives up.</summary>
    public void ExternalFailed(IClock clock)
    {
        ExternalAttempts++;
        if (ExternalAttempts >= MaxAttempts)
        {
            External = ExternalDelivery.Failed;
            ExternalNotBeforeUtc = null;
            return;
        }

        External = ExternalDelivery.Pending;
        ExternalNotBeforeUtc = clock.UtcNow.AddMinutes(ExternalAttempts);
    }

    /// <summary>No copy after all (the account was closed, or the contact is gone).</summary>
    public void ExternalCancelled()
    {
        External = ExternalDelivery.None;
        ExternalNotBeforeUtc = null;
    }
}
