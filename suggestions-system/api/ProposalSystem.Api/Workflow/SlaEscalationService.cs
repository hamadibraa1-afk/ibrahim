using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProposalSystem.Api.Common;
using ProposalSystem.Api.Data;
using ProposalSystem.Api.Domain;
using ProposalSystem.Api.Realtime;

namespace ProposalSystem.Api.Workflow;

public sealed record SlaRunResult(int Reminders, int Level1Escalations, int Level2Escalations, int ImpactWindowsOpened, int ImpactEscalations);

/// <summary>
/// One pass of SLA enforcement. For every proposal past its stage deadline:
/// <list type="bullet">
/// <item><b>Reminder</b> — the stage handlers and admins, at most once per ReminderRepeatHours.</item>
/// <item><b>Level 1</b> (deadline + 48h) — the handlers' line managers from the user directory are
/// notified, and a screening-stage proposal is rerouted to that manager, who may then screen it.</item>
/// <item><b>Level 2</b> (deadline + 96h) — executive management (all admins) is alerted.</item>
/// </list>
/// Each level fires once per stage; moving to the next stage resets it. The pass also opens
/// post-implementation impact windows and escalates overdue impact measurements.
/// Idempotent: running it twice in a row changes nothing the second time.
/// </summary>
public sealed class SlaEscalationService(
    AppDbContext db,
    NotificationService notifications,
    AuditTrail audit,
    IOptions<SlaOptions> options,
    TimeProvider clock,
    ILogger<SlaEscalationService> logger)
{
    private readonly SlaOptions _options = options.Value;

    public async Task<SlaRunResult> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var reminders = 0;
        var level1 = 0;
        var level2 = 0;

        var users = await db.Users.Where(u => u.Status == UserStatus.Active).ToListAsync(ct);
        var byId = users.ToDictionary(u => u.Id);
        var admins = users.Where(u => u.Role == UserRole.Admin).ToList();

        var breached = await db.Proposals
            .Include(p => p.Votes)
            .Where(p => ProposalAccess.SlaTrackedStatuses.Contains(p.Status) && p.SlaDueAt < now)
            .ToListAsync(ct);

        foreach (var p in breached)
        {
            var hoursOver = (int)Math.Floor((now - p.SlaDueAt).TotalHours);
            var handlers = Handlers(p, users, byId);
            var esc = _options.Escalation;
            var escalatedNow = false;

            if (esc.Enabled && hoursOver >= esc.Level1AfterHours && p.EscalationLevel < 1)
            {
                EscalateLevel1(p, handlers, byId, admins, users, hoursOver, now);
                level1++;
                escalatedNow = true;
            }

            if (esc.Enabled && hoursOver >= esc.Level2AfterHours && p.EscalationLevel < 2)
            {
                EscalateLevel2(p, admins, byId, hoursOver, now);
                level2++;
                escalatedNow = true;
            }

            if (escalatedNow)
            {
                // The escalation notice already told everyone; restart the reminder clock from it.
                p.LastSlaAlertAt = now;
            }
            else if (p.LastSlaAlertAt is null || (now - p.LastSlaAlertAt.Value).TotalHours >= _options.ReminderRepeatHours)
            {
                var recipients = handlers.Select(u => u.Id).Concat(admins.Select(a => a.Id)).Where(id => id != p.SubmitterId);
                notifications.Notify(recipients, NotificationTypes.SlaBreach,
                    $"تجاوز المهلة: المقترح {p.ProposalCode} «{p.Title}» متأخر {hoursOver} ساعة في مرحلة {StageName(p.Status)}.", p);
                p.LastSlaAlertAt = now;
                reminders++;
            }
        }

        var (opened, impactEscalations) = await RunImpactAsync(now, admins, byId, ct);

        await db.SaveChangesAsync(ct);
        await notifications.DispatchAsync(ct);

        var result = new SlaRunResult(reminders, level1, level2, opened, impactEscalations);
        if (result != new SlaRunResult(0, 0, 0, 0, 0))
            logger.LogInformation("SLA pass: {Result}", result);
        return result;
    }

    private void EscalateLevel1(Proposal p, List<User> handlers, Dictionary<int, User> byId, List<User> admins, List<User> users, int hoursOver, DateTime now)
    {
        // The reporting line comes from the internal user directory (User.ManagerId).
        var managers = handlers
            .Select(h => h.ManagerId is { } m && byId.TryGetValue(m, out var mgr) ? mgr : null)
            .OfType<User>()
            .Where(m => m.Id != p.SubmitterId)
            .DistinctBy(m => m.Id)
            .ToList();

        if (managers.Count == 0 && _options.Escalation.FallbackUserCode is { Length: > 0 } code
            && users.FirstOrDefault(u => string.Equals(u.UserCode, code, StringComparison.OrdinalIgnoreCase)) is { } fallback
            && fallback.Id != p.SubmitterId)
            managers.Add(fallback);

        if (managers.Count == 0)
            managers.AddRange(admins.Where(a => a.Id != p.SubmitterId));

        // Reroute only where a manager can lawfully act: screening. A manager cannot sign for a
        // committee member, and the executive stage already belongs to the admins.
        var reroute = _options.Escalation.RerouteScreeningToManager
                      && ProposalAccess.ScreeningStatuses.Contains(p.Status)
                      && managers.Count > 0;
        if (reroute)
            p.EscalatedToId = managers[0].Id;

        var note = string.Create(CultureInfo.InvariantCulture,
            $"تصعيد تلقائي (المستوى 1): تجاوز مهلة مرحلة {StageName(p.Status)} بـ {hoursOver} ساعة دون إجراء.")
            + (reroute ? $" أُعيد توجيه المقترح إلى {managers[0].ArabicName} لاتخاذ الإجراء." : "");

        p.EscalationLevel = 1;
        p.EscalatedAt = now;
        p.EscalationNote = note;
        audit.Record(p, null, AuditActions.AutoEscalated, p.Status, p.Status, note);

        notifications.Notify(managers.Select(m => m.Id), NotificationTypes.Escalation,
            reroute
                ? $"أُحيل إليك المقترح {p.ProposalCode} «{p.Title}» بعد تأخر الفرز {hoursOver} ساعة. يمكنك فرزه مباشرة."
                : $"تصعيد: المقترح {p.ProposalCode} «{p.Title}» متأخر {hoursOver} ساعة لدى فريقك في مرحلة {StageName(p.Status)}.",
            p);
        notifications.Notify(admins.Where(a => a.Id != p.SubmitterId && managers.All(m => m.Id != a.Id)).Select(a => a.Id),
            NotificationTypes.Escalation, $"تصعيد تلقائي (المستوى 1) للمقترح {p.ProposalCode} «{p.Title}».", p);
        notifications.ProposalChanged(p);
    }

    private void EscalateLevel2(Proposal p, List<User> admins, Dictionary<int, User> byId, int hoursOver, DateTime now)
    {
        // Executive management: every admin, plus their own managers where the directory has them.
        var recipients = admins
            .Concat(admins.Select(a => a.ManagerId is { } m && byId.TryGetValue(m, out var mgr) ? mgr : null).OfType<User>())
            .Where(u => u.Id != p.SubmitterId)
            .Select(u => u.Id)
            .Distinct()
            .ToList();

        var note = string.Create(CultureInfo.InvariantCulture,
            $"تصعيد تلقائي (المستوى 2 — الإدارة التنفيذية): تجاوز المهلة بـ {hoursOver} ساعة رغم التصعيد للمدير المباشر.");
        p.EscalationLevel = 2;
        p.EscalatedAt = now;
        p.EscalationNote = note;
        audit.Record(p, null, AuditActions.AutoEscalated, p.Status, p.Status, note);
        notifications.Notify(recipients, NotificationTypes.Escalation,
            $"تصعيد عاجل: المقترح {p.ProposalCode} «{p.Title}» متوقف {hoursOver} ساعة بعد مهلته في مرحلة {StageName(p.Status)}.", p);
        notifications.ProposalChanged(p);
    }

    /// <summary>The people expected to act at the proposal's current stage.</summary>
    private static List<User> Handlers(Proposal p, List<User> users, Dictionary<int, User> byId)
    {
        IEnumerable<User> handlers = p.Status switch
        {
            ProposalStatus.Submitted or ProposalStatus.UnderScreening when p.EscalatedToId is { } esc && byId.TryGetValue(esc, out var mgr) => [mgr],
            ProposalStatus.Submitted or ProposalStatus.UnderScreening => users.Where(u => u.Role == UserRole.Screener),
            ProposalStatus.WithCommittee => p.Votes.Where(v => !v.Signed).Select(v => byId.GetValueOrDefault(v.MemberId)).OfType<User>(),
            ProposalStatus.PendingExecutiveDecision => users.Where(u => u.Role == UserRole.Admin),
            _ => [],
        };
        var list = handlers.Where(h => h.Id != p.SubmitterId).ToList();
        // No one in the role at all: admins own the gap.
        return list.Count > 0 ? list : users.Where(u => u.Role == UserRole.Admin && u.Id != p.SubmitterId).ToList();
    }

    private async Task<(int Opened, int Escalated)> RunImpactAsync(DateTime now, List<User> admins, Dictionary<int, User> byId, CancellationToken ct)
    {
        var opened = 0;
        var escalated = 0;
        var adminIds = admins.Select(a => a.Id).ToList();

        var due = await db.ImpactAssessments
            .Include(i => i.Proposal)
            .Where(i => (i.Status == ImpactAssessmentStatus.Scheduled && i.OpensAt <= now)
                        || (i.Status == ImpactAssessmentStatus.Open && i.DueAt < now && i.EscalatedAt == null))
            .ToListAsync(ct);

        foreach (var impact in due)
        {
            var p = impact.Proposal;
            var measurers = p.OwnerId is { } owner ? new List<int> { owner } : adminIds;

            if (impact.Status == ImpactAssessmentStatus.Scheduled)
            {
                impact.Status = ImpactAssessmentStatus.Open;
                impact.OpenedNoticeAt = now;
                audit.Record(p, null, AuditActions.ImpactWindowOpened, notes: $"فُتحت نافذة قياس الأثر الفعلي حتى {impact.DueAt:yyyy-MM-dd}.");
                notifications.Notify(measurers, NotificationTypes.ImpactMeasurementDue,
                    $"حان موعد قياس الأثر الفعلي للمقترح المعتمد {p.ProposalCode} «{p.Title}». آخر موعد {impact.DueAt:yyyy-MM-dd}.", p);
                opened++;
            }

            if (impact.Status == ImpactAssessmentStatus.Open && impact.DueAt < now && impact.EscalatedAt is null)
            {
                impact.EscalatedAt = now;
                var ownerManager = p.OwnerId is { } o && byId.TryGetValue(o, out var ownerUser) && ownerUser.ManagerId is { } m ? m : (int?)null;
                var recipients = measurers.Concat(adminIds);
                if (ownerManager is { } mgr) recipients = recipients.Append(mgr);
                notifications.Notify(recipients, NotificationTypes.ImpactMeasurementOverdue,
                    $"تأخر قياس الأثر الفعلي للمقترح {p.ProposalCode} «{p.Title}» عن موعده ({impact.DueAt:yyyy-MM-dd}).", p);
                audit.Record(p, null, AuditActions.AutoEscalated, notes: "تصعيد تلقائي: تأخر قياس الأثر الفعلي بعد التطبيق.");
                escalated++;
            }
        }
        return (opened, escalated);
    }

    public static string StageName(ProposalStatus status) => status switch
    {
        ProposalStatus.Submitted or ProposalStatus.UnderScreening => "الفرز الأولي",
        ProposalStatus.WithCommittee => "لجنة الدراسة",
        ProposalStatus.PendingExecutiveDecision => "القرار التنفيذي",
        ProposalStatus.ReturnedForEdit => "التعديل",
        _ => "—",
    };
}

/// <summary>Runs <see cref="SlaEscalationService"/> on a fixed interval (15 minutes by default).</summary>
public sealed class SlaMonitorService(IServiceScopeFactory scopes, IOptions<SlaOptions> options, ILogger<SlaMonitorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, options.Value.CheckIntervalMinutes)));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<SlaEscalationService>().RunAsync(stoppingToken);
                await scope.ServiceProvider.GetRequiredService<Security.SessionService>().PurgeExpiredAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "SLA monitor pass failed; retrying on the next tick.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
