using System.Globalization;
using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Notifications;

/// <summary>
/// Looks for the facts nobody would otherwise notice: someone who has not come back from leave,
/// a document about to expire, a site with nobody on it today, decisions waiting too long.
/// Runs on a timer and is safe to run repeatedly — each fact is raised once.
/// </summary>
public sealed class NotificationWorker(IServiceProvider services, ILogger<NotificationWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
    private const int DocumentWarningDays = 45;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Notification scan failed; will retry on the next tick.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var today = clock.Today;

        await OverdueReturnsAsync(db, notifications, today, ct);
        await ExpiringDocumentsAsync(db, notifications, today, ct);
        await PendingDeductionsAsync(db, notifications, today, ct);
    }

    /// <summary>Leave ended, nobody reported back: the manager and HR should know today, not at payroll.</summary>
    private static async Task OverdueReturnsAsync(AppDbContext db, NotificationService notifications, DateOnly today, CancellationToken ct)
    {
        var overdue = await db.ReturnsToWork.AsNoTracking()
            .Where(r => r.IsActive && r.ActualDate == null && r.ExpectedDate < today).ToListAsync(ct);
        if (overdue.Count == 0) return;

        var names = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var profiles = await db.EmployeeProfiles.AsNoTracking().ToDictionaryAsync(p => p.UserId, ct);
        var hr = await HrRecipientsAsync(db, ct);

        foreach (var item in overdue)
        {
            var late = (today.DayNumber - item.ExpectedDate.DayNumber).ToString(CultureInfo.InvariantCulture);
            var subject = names.GetValueOrDefault(item.EmployeeId, "");
            var recipients = hr.ToList();
            if (profiles.TryGetValue(item.EmployeeId, out var profile) && profile.ManagerId is { } manager)
                recipients.Add(manager);

            foreach (var recipient in recipients.Distinct())
                await notifications.RaiseAsync(recipient, NotificationKind.ReturnOverdue,
                    [item.Id.ToString("N"), today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)], subject, late, "/hr/returns", item.Id, ct);
        }
    }

    /// <summary>A residency or passport expiring is an HR problem long before the day it expires.</summary>
    private static async Task ExpiringDocumentsAsync(AppDbContext db, NotificationService notifications, DateOnly today, CancellationToken ct)
    {
        var profiles = await db.EmployeeProfiles.AsNoTracking()
            .Where(p => p.Status != EmploymentStatus.Ended).ToListAsync(ct);
        var names = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var hr = await HrRecipientsAsync(db, ct);
        if (hr.Count == 0) return;

        foreach (var profile in profiles)
        {
            foreach (var (document, expiry) in profile.ExpiringDocuments(today, DocumentWarningDays))
            {
                foreach (var recipient in hr)
                    await notifications.RaiseAsync(recipient, NotificationKind.DocumentExpiring,
                        [profile.UserId.ToString("N"), document, expiry.ToString("yyyy-MM", CultureInfo.InvariantCulture)],
                        names.GetValueOrDefault(profile.UserId, ""), expiry.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        "/hr/employees", profile.Id, ct);
            }
        }
    }

    /// <summary>Undecided proposals block the month's payroll, so they are surfaced daily.</summary>
    private static async Task PendingDeductionsAsync(AppDbContext db, NotificationService notifications, DateOnly today, CancellationToken ct)
    {
        var pending = await db.DeductionProposals.CountAsync(p => p.IsActive && p.Status == DeductionStatus.Proposed, ct);
        if (pending == 0) return;

        foreach (var recipient in await HrRecipientsAsync(db, ct))
            await notifications.RaiseAsync(recipient, NotificationKind.DeductionProposed,
                [today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)], null,
                pending.ToString(CultureInfo.InvariantCulture), "/hr/discipline", null, ct);
    }

    private static async Task<List<Guid>> HrRecipientsAsync(AppDbContext db, CancellationToken ct) =>
        await db.Users.AsNoTracking()
            .Where(u => u.IsActive && (u.Role == UserRole.HrManager || u.Role == UserRole.HrOfficer))
            .Select(u => u.Id).ToListAsync(ct);
}
