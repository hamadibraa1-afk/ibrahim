using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Notifications;

/// <summary>
/// Delivers a notification outside the application. In-app alerts need no channel; email and
/// SMS do, and the provider differs per organisation — so the system depends on this interface
/// and not on a vendor. The logging adapter below keeps everything working until one is wired.
/// </summary>
public interface INotificationChannel
{
    NotificationChannel Channel { get; }

    Task SendAsync(User recipient, Notification notification, CancellationToken ct);
}

/// <summary>Records what would have been sent. Replaced by a real provider without touching callers.</summary>
public sealed class LoggingEmailChannel(ILogger<LoggingEmailChannel> logger) : INotificationChannel
{
    public NotificationChannel Channel => NotificationChannel.Email;

    public Task SendAsync(User recipient, Notification notification, CancellationToken ct)
    {
        logger.LogInformation("Email notification {Kind} for {Employee} ({Email}) — not sent: no provider configured.",
            notification.Kind, recipient.EmployeeNumber, recipient.Email);
        return Task.CompletedTask;
    }
}

public sealed class LoggingSmsChannel(ILogger<LoggingSmsChannel> logger) : INotificationChannel
{
    public NotificationChannel Channel => NotificationChannel.Sms;

    public Task SendAsync(User recipient, Notification notification, CancellationToken ct)
    {
        logger.LogInformation("SMS notification {Kind} for {Employee} ({Phone}) — not sent: no provider configured.",
            notification.Kind, recipient.EmployeeNumber, recipient.Phone);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Raises alerts and hands them to the channels the settings allow.
/// Every alert carries a stable key, so a check that runs hourly cannot flood the bell:
/// the same fact is stored once and ignored afterwards.
/// </summary>
public sealed class NotificationService(AppDbContext db, IEnumerable<INotificationChannel> channels,
    ILogger<NotificationService> logger, IClock clock)
{
    public async Task<bool> RaiseAsync(Guid recipientId, NotificationKind kind, string[] keyParts,
        string? subject = null, string? value = null, string? link = null, Guid? referenceId = null,
        CancellationToken ct = default)
    {
        if (recipientId == Guid.Empty) return false;

        var key = Notification.KeyFor(kind, recipientId, keyParts);
        if (await db.Notifications.AnyAsync(n => n.DedupeKey == key, ct)) return false;

        var setting = await db.NotificationSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Kind == kind, ct);
        if (setting is not null && !setting.InApp && !setting.Email && !setting.Sms) return false;

        var notification = new Notification(recipientId, kind, key, clock.Now, subject, value, link, referenceId);
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(ct);

        await DispatchAsync(notification, setting, ct);
        return true;
    }

    public async Task MarkReadAsync(Guid recipientId, IReadOnlyCollection<Guid>? ids, CancellationToken ct)
    {
        var query = db.Notifications.Where(n => n.RecipientId == recipientId && n.ReadAt == null);
        if (ids is { Count: > 0 }) query = query.Where(n => ids.Contains(n.Id));

        foreach (var notification in await query.ToListAsync(ct)) notification.MarkRead(clock.Now);
        await db.SaveChangesAsync(ct);
    }

    private async Task DispatchAsync(Notification notification, NotificationSetting? setting, CancellationToken ct)
    {
        var wanted = channels.Where(c => c.Channel != NotificationChannel.InApp
                                         && (setting?.Uses(c.Channel) ?? false)).ToList();
        if (wanted.Count == 0) return;

        var recipient = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == notification.RecipientId, ct);
        if (recipient is null) return;

        foreach (var channel in wanted)
        {
            try
            {
                await channel.SendAsync(recipient, notification, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A provider being down must never break the action that produced the alert.
                logger.LogWarning(ex, "Notification channel {Channel} failed for {Kind}.", channel.Channel, notification.Kind);
            }
        }
    }
}
