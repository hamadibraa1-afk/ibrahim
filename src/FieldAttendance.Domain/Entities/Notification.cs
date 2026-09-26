using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;

namespace FieldAttendance.Domain.Entities;

/// <summary>
/// One alert for one person.
///
/// The text is not stored: the kind and a few values are, and the client renders them in the
/// reader's own language. A stored Arabic sentence would be wrong the moment someone switches
/// to English, and unchangeable once written.
///
/// <see cref="DedupeKey"/> is what keeps a recurring check from filling the bell with copies:
/// the same fact about the same day produces the same key, and the second attempt is ignored.
/// </summary>
public sealed class Notification : Entity
{
    private Notification() { } // EF Core

    public Notification(Guid recipientId, NotificationKind kind, string dedupeKey, DateTimeOffset createdAt,
        string? subject = null, string? value = null, string? link = null, Guid? referenceId = null)
    {
        RecipientId = Guard.NotEmpty(recipientId, "notification.recipient");
        Kind = kind;
        DedupeKey = Guard.Required(dedupeKey, "notification.key", 200);
        CreatedAtUtc = createdAt;
        Subject = Guard.Optional(subject, "notification.subject", 200);
        Value = Guard.Optional(value, "notification.value", 100);
        Link = Guard.Optional(link, "notification.link", 300);
        ReferenceId = referenceId;
    }

    public Guid RecipientId { get; private set; }
    public NotificationKind Kind { get; private set; }

    /// <summary>Stable identity of the underlying fact, so the same alert is never raised twice.</summary>
    public string DedupeKey { get; private set; } = string.Empty;

    /// <summary>Free text the reader needs, usually a person's or a site's name.</summary>
    public string? Subject { get; private set; }

    /// <summary>A number or date the message needs, rendered by the client.</summary>
    public string? Value { get; private set; }

    public string? Link { get; private set; }
    public Guid? ReferenceId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    public bool IsUnread => ReadAt is null;

    public void MarkRead(DateTimeOffset at) => ReadAt ??= at;

    /// <summary>Builds the identity of a fact: kind, who it is for, and what it is about.</summary>
    public static string KeyFor(NotificationKind kind, Guid recipientId, params string[] parts) =>
        string.Join(':', new[] { kind.ToString(), recipientId.ToString("N") }.Concat(parts));
}

/// <summary>
/// Which kinds reach a person, and through which channel. Absence of a row means the default:
/// in-app only. Email and SMS are delivered by whatever adapter is configured, so turning one
/// on later is configuration rather than code.
/// </summary>
public sealed class NotificationSetting : Entity
{
    private NotificationSetting() { } // EF Core

    public NotificationSetting(NotificationKind kind, bool inApp, bool email, bool sms)
    {
        Kind = kind;
        Update(inApp, email, sms);
    }

    public NotificationKind Kind { get; private set; }
    public bool InApp { get; private set; } = true;
    public bool Email { get; private set; }
    public bool Sms { get; private set; }

    public void Update(bool inApp, bool email, bool sms)
    {
        InApp = inApp;
        Email = email;
        Sms = sms;
    }

    public bool Uses(NotificationChannel channel) => channel switch
    {
        NotificationChannel.InApp => InApp,
        NotificationChannel.Email => Email,
        _ => Sms,
    };
}
