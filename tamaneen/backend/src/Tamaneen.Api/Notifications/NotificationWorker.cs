using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Crisis;
using Tamaneen.Hosting;
using Tamaneen.Identity.Notifications;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;
using Tamaneen.SharedKernel.Notifications;

namespace Tamaneen.Api.Notifications;

public sealed class NotificationOptions
{
    public const string Section = "Notifications";

    /// <summary>Runs the loop below. Off in the Testing environment, where tests call <see cref="NotificationWorker.RunOnceAsync"/>.</summary>
    public bool WorkerEnabled { get; set; } = true;

    public int PollSeconds { get; set; } = 60;
}

/// <summary>
/// Background jobs of the notification service, once a minute:
/// <list type="number">
///   <item>appointment reminders 24 hours and 1 hour before (guide, patient journey step 15);</item>
///   <item>after a crisis event closes, the patient's follow-up notice that opens P38 and the treating therapist's notice;</item>
///   <item>SMS and e-mail copies held by quiet hours or a failed send.</item>
/// </list>
/// Each reminder and follow-up carries a dedupe key that is unique per recipient in the database, so a restart,
/// a second instance or an overlapping run never sends one twice. A moved appointment gets new keys (the start
/// time is part of the key); a cancelled one gets nothing more.
/// </summary>
public sealed partial class NotificationWorker(IServiceScopeFactory scopeFactory, Notifier notifier, IClock clock, IOptions<NotificationOptions> options, ILogger<NotificationWorker> log) : BackgroundService
{
    public static readonly TimeSpan DayBefore = TimeSpan.FromHours(24);
    public static readonly TimeSpan HourBefore = TimeSpan.FromHours(1);

    /// <summary>Closed events older than this get no follow-up notice (e.g. after a long outage).</summary>
    public static readonly TimeSpan FollowUpWindow = TimeSpan.FromDays(3);

    private ScopeRunner Scopes => new(scopeFactory);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WorkerEnabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.PollSeconds)));
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogRunFailed(log, ex.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    public async Task<WorkerRun> RunOnceAsync(CancellationToken ct)
    {
        var reminders = await RemindersAsync(ct).ConfigureAwait(false);
        var followUps = await CrisisFollowUpsAsync(ct).ConfigureAwait(false);
        var copies = await HeldCopiesAsync(ct).ConfigureAwait(false);
        return new WorkerRun(reminders, followUps, copies);
    }

    public static string ReminderKey(string kind, Guid appointmentId, DateTimeOffset startsAt) =>
        $"{kind}:{appointmentId:N}:{startsAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}";

    private async Task<int> RemindersAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var due = await Scopes.AsServiceAsync(Notifier.ServiceId, AppRoles.NotificationService, async sp =>
        {
            var db = sp.GetRequiredService<TamaneenDbContext>();
            var until = now + DayBefore;
            var appointments = await db.Appointments.AsNoTracking()
                .Where(a => a.Status == AppointmentStatus.Confirmed && a.StartsAtUtc > now && a.StartsAtUtc <= until)
                .Select(a => new { a.Id, a.PatientId, a.TherapistUserId, a.StartsAtUtc }).ToListAsync(ct).ConfigureAwait(false);
            var wanted = appointments.Select(a =>
            {
                var kind = a.StartsAtUtc - now <= HourBefore ? NotificationKinds.AppointmentReminder1h : NotificationKinds.AppointmentReminder24h;
                return (a.Id, a.PatientId, a.TherapistUserId, a.StartsAtUtc, Kind: kind, Key: ReminderKey(kind, a.Id, a.StartsAtUtc));
            }).ToList();
            var keys = wanted.Select(w => w.Key).ToList();
            var done = (await db.Notifications.AsNoTracking().Where(n => n.DedupeKey != null && keys.Contains(n.DedupeKey)).Select(n => n.DedupeKey!).ToListAsync(ct).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
            return wanted.Where(w => !done.Contains(w.Key)).ToList();
        }).ConfigureAwait(false);

        var sent = 0;
        foreach (var r in due)
        {
            var data = new Dictionary<string, string>
            {
                ["appointmentId"] = r.Id.ToString(),
                ["startsAt"] = r.StartsAtUtc.ToString("O", CultureInfo.InvariantCulture),
                ["therapistId"] = r.TherapistUserId.ToString(),
            };
            if (await notifier.NotifyAsync(new Notification(r.PatientId, r.Kind, data), r.Key, ct).ConfigureAwait(false))
            {
                sent++;
            }
        }

        return sent;
    }

    private async Task<int> CrisisFollowUpsAsync(CancellationToken ct)
    {
        var since = clock.UtcNow - FollowUpWindow;
        var closed = await Scopes.AsServiceAsync(Notifier.ServiceId, AppRoles.NotificationService, async sp =>
        {
            var db = sp.GetRequiredService<TamaneenDbContext>();
            var events = await db.CrisisEvents.AsNoTracking().Where(e => e.Status == CrisisEventStatus.Closed && e.ClosedAtUtc >= since)
                .Select(e => new { e.Id, e.PatientId, e.PseudonymId, e.Number }).ToListAsync(ct).ConfigureAwait(false);
            var keys = events.Select(e => FollowUpKey(e.Id)).ToList();
            var done = (await db.Notifications.AsNoTracking().Where(n => n.DedupeKey != null && keys.Contains(n.DedupeKey)).Select(n => n.DedupeKey!).ToListAsync(ct).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
            return events.Where(e => !done.Contains(FollowUpKey(e.Id))).ToList();
        }).ConfigureAwait(false);

        var count = 0;
        foreach (var e in closed)
        {
            // The treating therapists are read as the patient: the notification service does not see care assignments.
            var therapists = await Scopes.AsUserAsync(e.PatientId, AppRoles.Patient, SupportedLanguages.Default, sp =>
                sp.GetRequiredService<TamaneenDbContext>().CareAssignments.AsNoTracking().Where(a => a.PatientId == e.PatientId && a.EndedAtUtc == null)
                    .Select(a => a.TherapistUserId).Distinct().ToListAsync(ct)).ConfigureAwait(false);
            foreach (var therapist in therapists)
            {
                await notifier.NotifyAsync(
                    new Notification(therapist, NotificationKinds.CrisisClosed, new Dictionary<string, string> { ["eventId"] = e.Id.ToString(), ["pseudonymId"] = e.PseudonymId, ["number"] = $"CE-{e.Number}" }),
                    FollowUpKey(e.Id),
                    ct).ConfigureAwait(false);
            }

            // Last, because its key marks the event as handled.
            if (await notifier.NotifyAsync(new Notification(e.PatientId, NotificationKinds.CrisisFollowUp, new Dictionary<string, string> { ["eventId"] = e.Id.ToString() }), FollowUpKey(e.Id), ct).ConfigureAwait(false))
            {
                count++;
            }
        }

        return count;
    }

    public static string FollowUpKey(Guid eventId) => $"crisis.follow_up:{eventId:N}";

    private async Task<int> HeldCopiesAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var held = await Scopes.AsServiceAsync(Notifier.ServiceId, AppRoles.NotificationService, async sp =>
        {
            var db = sp.GetRequiredService<TamaneenDbContext>();
            var rows = await db.Notifications.AsNoTracking().Where(n => n.External == ExternalDelivery.Pending && n.ExternalNotBeforeUtc <= now)
                .OrderBy(n => n.ExternalNotBeforeUtc).Take(100).Select(n => new { n.Id, n.RecipientUserId }).ToListAsync(ct).ConfigureAwait(false);
            var claimed = new List<(Guid Id, Guid Recipient)>();
            foreach (var r in rows)
            {
                // Claim it, so a second instance running at the same moment skips it.
                var won = await db.Notifications.Where(n => n.Id == r.Id && n.External == ExternalDelivery.Pending)
                    .ExecuteUpdateAsync(u => u.SetProperty(n => n.External, ExternalDelivery.Sending), ct).ConfigureAwait(false);
                if (won == 1)
                {
                    claimed.Add((r.Id, r.RecipientUserId));
                }
            }

            return claimed;
        }).ConfigureAwait(false);

        foreach (var (id, recipient) in held)
        {
            await notifier.DeliverAsync(recipient, id, ct).ConfigureAwait(false);
        }

        return held.Count;
    }

    [LoggerMessage(EventId = 4403, Level = LogLevel.Warning, Message = "A notification run failed ({Error}).")]
    private static partial void LogRunFailed(ILogger logger, string error);
}

public sealed record WorkerRun(int Reminders, int CrisisFollowUps, int HeldCopies);
