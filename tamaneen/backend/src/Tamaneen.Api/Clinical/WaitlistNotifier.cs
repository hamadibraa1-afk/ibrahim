using Microsoft.EntityFrameworkCore;
using Tamaneen.Api.Auth;
using Tamaneen.Api.Endpoints;
using Tamaneen.Api.Patients;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Hosting;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.Infrastructure.Security;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Clinical;

/// <summary>
/// P56 «تم التوفر»: when bookable times appear (a cancellation, a move, a released hold, a patient joining), the
/// waiting patients are matched against them, risk first and then by time waited, one patient per free time.
/// The match runs as the scheduling service, which row-level security lets read the queue; the message is
/// composed and sent as the patient, the only identity allowed to decrypt their contact. A failed send is
/// logged and never fails the request that freed the time: the app shows the notice on P56 either way.
/// </summary>
public sealed partial class WaitlistNotifier(ScopeRunner scopes, IMessageSender sender, MessageComposer composer, IClock clock, ILogger<WaitlistNotifier> log)
{
    /// <summary>How far ahead free times count: the booking screen shows two weeks.</summary>
    public static readonly TimeSpan Horizon = TimeSpan.FromDays(14);

    public async Task<int> CheckAsync(CancellationToken ct)
    {
        List<WaitlistEntry> notified;
        try
        {
            notified = await scopes.AsServiceAsync(SchedulingEndpoints.SchedulingServiceId, AppRoles.SchedulingService, sp => MatchAsync(sp, ct)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCheckFailed(log, ex.GetType().Name);
            return 0;
        }

        foreach (var entry in notified)
        {
            await SendAsync(entry.PatientId, ct).ConfigureAwait(false);
        }

        return notified.Count;
    }

    private async Task<List<WaitlistEntry>> MatchAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<TamaneenDbContext>();
        var waiting = await db.WaitlistEntries.Where(w => w.Status == WaitlistStatus.Waiting)
            .OrderByDescending(w => w.RiskLevel).ThenBy(w => w.JoinedAtUtc).ToListAsync(ct).ConfigureAwait(false);
        if (waiting.Count == 0)
        {
            return [];
        }

        var now = clock.UtcNow;
        var earliest = now + TherapistDirectory.MinimumNotice;
        var until = now + Horizon;
        var therapists = await db.TherapistProfiles.AsNoTracking().Where(t => t.AcceptingNewPatients && t.LicenseVerified).ToDictionaryAsync(t => t.UserId, ct).ConfigureAwait(false);
        var ids = therapists.Keys.ToList();
        var slots = await db.AvailabilitySlots.AsNoTracking()
            .Where(s => ids.Contains(s.TherapistUserId) && s.StartsAtUtc > earliest && s.StartsAtUtc <= until
                        && (s.Status == SlotStatus.Open || (s.Status == SlotStatus.Held && s.HeldUntilUtc <= now)))
            .OrderBy(s => s.StartsAtUtc).Select(s => new { s.Id, s.TherapistUserId, s.StartsAtUtc }).ToListAsync(ct).ConfigureAwait(false);

        // A time already offered to someone still holding the notice is not offered again.
        var offered = (await db.WaitlistEntries.Where(w => w.Status == WaitlistStatus.Notified && w.NotifiedSlotId != null).Select(w => w.NotifiedSlotId!.Value).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        var notified = new List<WaitlistEntry>();
        foreach (var entry in waiting)
        {
            var slot = slots.FirstOrDefault(s => !offered.Contains(s.Id) && entry.Fits(therapists[s.TherapistUserId], s.StartsAtUtc));
            if (slot is null)
            {
                continue;
            }

            entry.MarkNotified(slot.Id, clock);
            offered.Add(slot.Id);
            notified.Add(entry);
        }

        if (notified.Count > 0)
        {
            var audit = sp.GetRequiredService<IAuditLog>();
            foreach (var entry in notified)
            {
                audit.Append("waitlist.notified", "WaitlistEntry", entry.Id.ToString(), null, $"slot={entry.NotifiedSlotId}");
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return notified;
    }

    private async Task SendAsync(Guid patientId, CancellationToken ct)
    {
        try
        {
            var message = await scopes.AsUserAsync<OutgoingMessage?>(patientId, AppRoles.Patient, SupportedLanguages.Default, async sp =>
            {
                var db = sp.GetRequiredService<TamaneenDbContext>();
                var profile = await db.PatientProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.PatientId == patientId, ct).ConfigureAwait(false);
                if (profile?.ContactCipher is null)
                {
                    return null;
                }

                var language = await db.UserAccounts.Where(a => a.Id == patientId).Select(a => a.PreferredLanguage).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? SupportedLanguages.Default;
                var contact = await sp.GetRequiredService<IPatientDataProtector>().DecryptAsync(patientId, Fields.Contact, profile.ContactCipher, ct).ConfigureAwait(false);
                return composer.Compose(profile.ContactType, contact, language, MessagePurpose.WaitlistSlotOpen, null);
            }).ConfigureAwait(false);
            if (message is not null)
            {
                await sender.SendAsync(message, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSendFailed(log, ex.GetType().Name);
        }
    }

    [LoggerMessage(EventId = 4301, Level = LogLevel.Warning, Message = "The waitlist check failed ({Error}).")]
    private static partial void LogCheckFailed(ILogger logger, string error);

    [LoggerMessage(EventId = 4302, Level = LogLevel.Warning, Message = "Sending a waitlist notice failed ({Error}).")]
    private static partial void LogSendFailed(ILogger logger, string error);
}
