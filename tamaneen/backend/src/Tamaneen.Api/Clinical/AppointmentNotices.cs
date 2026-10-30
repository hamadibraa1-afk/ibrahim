using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;
using Tamaneen.SharedKernel.Notifications;

namespace Tamaneen.Api.Clinical;

/// <summary>
/// The notifications S18 raises, through <see cref="INotifier"/> (the notification service sends them). Data holds
/// ids, times and the therapist's display name only. A failed notice is logged and never fails the request.
/// </summary>
public sealed partial class AppointmentNotices(INotifier notifier, TamaneenDbContext db, ILogger<AppointmentNotices> log)
{
    public const string CancelledByTherapist = "appointment.cancelled_by_therapist";
    public const string RunningLate = "appointment.running_late";
    public const string Missed = "appointment.missed";
    public const string Rescheduled = "appointment.rescheduled";
    public const string Booked = "appointment.booked";

    /// <summary>EC-52: a therapist cancelled and no substitute was offered; supervisors rebook within 48 hours.</summary>
    public const string RebookNeeded = "appointment.rebook_needed";

    /// <summary>EC-53 and §11.1: a second missed session in a row, or a missed session at risk 3–4.</summary>
    public const string NoShowFollowUp = "appointment.no_show_followup";

    public Task CancelledAsync(Appointment a, string pseudonymId, CancellationToken ct) => SendAsync(async () =>
    {
        await ToPatientAsync(a, CancelledByTherapist, $"/appointments/{a.Id}/cancelled", ct).ConfigureAwait(false);
        await ToSupervisorsAsync(RebookNeeded, a, pseudonymId, $"/patients/{pseudonymId}", ct).ConfigureAwait(false);
    });

    /// <summary>S33 «حجز الجلسة القادمة»: the therapist booked the next session; the patient sees it in P27.</summary>
    public Task BookedAsync(Appointment a, CancellationToken ct) => SendAsync(() =>
        ToPatientAsync(a, Booked, $"/appointments/{a.Id}", ct));

    /// <summary>S18 reschedule by the therapist: the patient sees the new time (P27 detail).</summary>
    public Task RescheduledAsync(Appointment a, CancellationToken ct) => SendAsync(() =>
        ToPatientAsync(a, Rescheduled, $"/appointments/{a.Id}", ct));

    public Task LateAsync(Appointment a, CancellationToken ct) => SendAsync(() =>
        ToPatientAsync(a, RunningLate, $"/appointments/{a.Id}/lobby", ct, ("minutes", a.TherapistLateMinutes!.Value.ToString(CultureInfo.InvariantCulture))));

    public Task MissedAsync(Appointment a, string pseudonymId, bool followUp, CancellationToken ct) => SendAsync(async () =>
    {
        await ToPatientAsync(a, Missed, $"/appointments/{a.Id}/missed", ct).ConfigureAwait(false);
        if (followUp)
        {
            await ToSupervisorsAsync(NoShowFollowUp, a, pseudonymId, $"/patients/{pseudonymId}", ct).ConfigureAwait(false);
        }
    });

    private async Task ToPatientAsync(Appointment a, string kind, string link, CancellationToken ct, params (string Key, string Value)[] extra)
    {
        var language = await db.UserAccounts.AsNoTracking().Where(u => u.Id == a.PatientId).Select(u => u.PreferredLanguage).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? SupportedLanguages.Default;
        var staff = await db.StaffProfiles.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == a.TherapistUserId, ct).ConfigureAwait(false);
        var data = Base(a);
        data["therapistName"] = staff?.DisplayName(language) ?? string.Empty;
        foreach (var (key, value) in extra)
        {
            data[key] = value;
        }

        await notifier.NotifyAsync(new Notification(a.PatientId, kind, data, link), ct).ConfigureAwait(false);
    }

    private async Task ToSupervisorsAsync(string kind, Appointment a, string pseudonymId, string link, CancellationToken ct)
    {
        var supervisors = await db.UserAccounts.AsNoTracking().Where(u => u.Role == AppRoles.Supervisor).Select(u => u.Id).ToListAsync(ct).ConfigureAwait(false);
        var data = Base(a);
        data["pseudonymId"] = pseudonymId;
        data["therapistUserId"] = a.TherapistUserId.ToString();
        foreach (var id in supervisors)
        {
            await notifier.NotifyAsync(new Notification(id, kind, data, link), ct).ConfigureAwait(false);
        }
    }

    private static Dictionary<string, string> Base(Appointment a) => new(StringComparer.Ordinal)
    {
        ["appointmentId"] = a.Id.ToString(),
        ["startsAt"] = a.StartsAtUtc.ToString("O", CultureInfo.InvariantCulture),
    };

    private async Task SendAsync(Func<Task> send)
    {
        try
        {
            await send().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(log, ex.GetType().Name);
        }
    }

    [LoggerMessage(EventId = 4311, Level = LogLevel.Warning, Message = "An appointment notification failed ({Error}).")]
    private static partial void LogFailed(ILogger logger, string error);
}
