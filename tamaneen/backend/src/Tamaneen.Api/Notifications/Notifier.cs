using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Tamaneen.Api.Auth;
using Tamaneen.Api.Patients;
using Tamaneen.Hosting;
using Tamaneen.Identity;
using Tamaneen.Identity.Notifications;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.Infrastructure.Security;
using Tamaneen.SharedKernel;
using Tamaneen.SharedKernel.Notifications;

namespace Tamaneen.Api.Notifications;

/// <summary>
/// The notification service. Every notice is stored in the recipient's in-app inbox (P46, S41). A patient may
/// also get a neutral SMS or e-mail copy on the contact they signed up with, following P33:
/// <list type="bullet">
///   <item>the channel switch for that contact, or the SMS fallback for important kinds while app notifications are on;</item>
///   <item>quiet hours in the patient's time zone hold the copy until they end, except for crisis kinds, which always go at once;</item>
///   <item>"neutral notifications" replace the text with a line that says nothing at all.</item>
/// </list>
/// Staff have no readable contact on file (only a lookup hash), so their notices are in-app; risk alerts reach
/// them through the crisis worker's paging. The inbox row is written by the notification service identity;
/// the copy is composed as the patient, the only identity that may decrypt their contact. A failure is logged
/// and never fails the caller's request.
/// </summary>
public sealed partial class Notifier(IServiceScopeFactory scopeFactory, IClock clock, ILogger<Notifier> log) : INotifier
{
    public static readonly Guid ServiceId = new("7a3c9e10-4b2d-4f6a-8c1e-2d5b9f0a6e73");

    private const int MaxValueLength = 200;

    private ScopeRunner Scopes => new(scopeFactory);

    public Task NotifyAsync(Notification notification, CancellationToken ct) => NotifyAsync(notification, null, ct);

    /// <summary>Stores the notice and dispatches its copy. With a dedupe key, a second call for the same recipient does nothing and returns false.</summary>
    public async Task<bool> NotifyAsync(Notification notification, string? dedupeKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notification);
        try
        {
            var kind = NotificationCatalog.Find(notification.Kind);
            var data = Sanitize(notification.Data);
            var stored = await Scopes.AsServiceAsync(ServiceId, AppRoles.NotificationService, async sp =>
            {
                var db = sp.GetRequiredService<TamaneenDbContext>();
                var account = await db.UserAccounts.AsNoTracking().Where(a => a.Id == notification.RecipientUserId)
                    .Select(a => new { a.Role, a.Status }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                if (account is null || account.Status == AccountStatus.Closed)
                {
                    return (Guid?)null;
                }

                var staff = account.Role != AppRoles.Patient;
                if (staff && kind.StaffBookings)
                {
                    var prefs = await db.NotificationPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == notification.RecipientUserId, ct).ConfigureAwait(false);
                    if (prefs is { StaffBookings: false })
                    {
                        return null;
                    }
                }

                var row = UserNotification.Create(
                    notification.RecipientUserId,
                    kind.Code,
                    kind.Category,
                    JsonSerializer.Serialize(data),
                    notification.Link ?? kind.DefaultLink(data, staff),
                    dedupeKey,
                    clock);
                db.Notifications.Add(row);
                try
                {
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                }
                catch (DbUpdateException ex) when (dedupeKey is not null && IsDuplicate(ex))
                {
                    return null;
                }

                return staff ? Guid.Empty : row.Id;
            }).ConfigureAwait(false);

            if (stored is null)
            {
                return false;
            }

            if (stored != Guid.Empty)
            {
                await DeliverAsync(notification.RecipientUserId, stored.Value, ct).ConfigureAwait(false);
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogNotifyFailed(log, notification.Kind, ex.GetType().Name);
            return false;
        }
    }

    /// <summary>
    /// Sends, holds or skips the SMS / e-mail copy of one of a patient's notices. Runs as the patient. Called right
    /// after the notice is stored and again by <see cref="NotificationWorker"/> for copies held by quiet hours or a failed send.
    /// </summary>
    public async Task DeliverAsync(Guid patientId, Guid notificationId, CancellationToken ct)
    {
        try
        {
            await Scopes.AsUserAsync(patientId, AppRoles.Patient, SupportedLanguages.Default, async sp =>
            {
                var db = sp.GetRequiredService<TamaneenDbContext>();
                var row = await db.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId, ct).ConfigureAwait(false);
                if (row is null || row.External is ExternalDelivery.Sent or ExternalDelivery.Failed)
                {
                    return;
                }

                var kind = NotificationCatalog.Find(row.Kind);
                var prefs = await db.NotificationPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == patientId, ct).ConfigureAwait(false)
                    ?? NotificationPreferences.Default(patientId);
                var profile = await db.PatientProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.PatientId == patientId, ct).ConfigureAwait(false);
                var account = await db.UserAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == patientId, ct).ConfigureAwait(false);
                if (profile?.ContactCipher is null || profile.IsShredded || account is null || account.Status == AccountStatus.Closed)
                {
                    row.ExternalCancelled();
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    return;
                }

                var channel = profile.ContactType == "email" ? "email" : "sms";
                if (!WantsCopy(kind, prefs, channel))
                {
                    row.ExternalCancelled();
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    return;
                }

                var zone = NotificationTexts.FindZone(profile.TimeZone);
                var local = TimeZoneInfo.ConvertTime(clock.UtcNow, zone).DateTime;
                if (!kind.Crisis && prefs.IsQuiet(TimeOnly.FromDateTime(local)))
                {
                    var end = prefs.QuietEndsAfter(local);
                    row.QueueExternal(channel, new DateTimeOffset(end, zone.GetUtcOffset(end)).ToUniversalTime());
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    return;
                }

                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(row.DataJson) ?? [];
                var text = sp.GetRequiredService<NotificationTexts>().External(row.Kind, data, account.PreferredLanguage, profile.TimeZone, prefs.NeutralText);
                var contact = await sp.GetRequiredService<IPatientDataProtector>().DecryptAsync(patientId, Fields.Contact, profile.ContactCipher, ct).ConfigureAwait(false);
                var purpose = row.Kind == NotificationKinds.WaitlistSlotOpen ? MessagePurpose.WaitlistSlotOpen : MessagePurpose.Notification;
                try
                {
                    await sp.GetRequiredService<IMessageSender>().SendAsync(new OutgoingMessage(channel, contact, account.PreferredLanguage, purpose, text), ct).ConfigureAwait(false);
                    row.ExternalSent(clock);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogSendFailed(log, row.Kind, ex.GetType().Name);
                    row.QueueExternal(channel, clock.UtcNow);
                    row.ExternalFailed(clock);
                }

                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogNotifyFailed(log, "deliver", ex.GetType().Name);
        }
    }

    /// <summary>Crisis kinds always; otherwise the switch for the contact's channel, or the SMS fallback for important kinds.</summary>
    public static bool WantsCopy(NotificationKind kind, NotificationPreferences prefs, string channel) =>
        kind.Crisis || (channel == "email" ? prefs.Email : prefs.Sms) || (kind.Important && prefs.Push);

    /// <summary>Keeps short, plain values only; anything else (long text, odd keys) is dropped so free text cannot slip in.</summary>
    internal static Dictionary<string, string> Sanitize(IReadOnlyDictionary<string, string>? data)
    {
        var clean = new Dictionary<string, string>(StringComparer.Ordinal);
        if (data is null)
        {
            return clean;
        }

        foreach (var (key, value) in data)
        {
            if (KeyPattern().IsMatch(key) && value is { Length: > 0 and <= MaxValueLength } && !value.Contains('\n', StringComparison.Ordinal))
            {
                clean[key] = value;
            }
        }

        return clean;
    }

    private static bool IsDuplicate(DbUpdateException ex) => ex.InnerException is SqlException { Number: 2601 or 2627 };

    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9]{0,39}$")]
    private static partial Regex KeyPattern();

    [LoggerMessage(EventId = 4401, Level = LogLevel.Warning, Message = "A notification ({Kind}) could not be stored or sent ({Error}).")]
    private static partial void LogNotifyFailed(ILogger logger, string kind, string error);

    [LoggerMessage(EventId = 4402, Level = LogLevel.Warning, Message = "Sending the copy of a notification ({Kind}) failed ({Error}); it will be retried.")]
    private static partial void LogSendFailed(ILogger logger, string kind, string error);
}
