using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Attendance;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Scheduling;
using FieldAttendance.Domain.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FieldAttendance.Api.Features.Attendance;

public sealed class ScheduleSnapshotLoader(AppDbContext db)
{
    /// <param name="employeeIds">null = all active collectors.</param>
    public async Task<ScheduleSnapshot> LoadAsync(IReadOnlyCollection<Guid>? employeeIds, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var assignments = db.Assignments.AsNoTracking()
            .Where(a => a.IsActive && a.StartDate <= to && (a.EndDate == null || a.EndDate >= from));
        var overrides = db.AssignmentOverrides.AsNoTracking()
            .Where(o => o.IsActive && o.FromDate <= to && o.ToDate >= from);
        var leaves = db.LeaveRequests.AsNoTracking()
            .Where(l => l.IsActive && l.Status == RequestStatus.Approved && l.FromDate <= to && l.ToDate >= from);

        if (employeeIds is not null)
        {
            assignments = assignments.Where(a => employeeIds.Contains(a.EmployeeId));
            overrides = overrides.Where(o => employeeIds.Contains(o.EmployeeId));
            leaves = leaves.Where(l => employeeIds.Contains(l.EmployeeId));
        }

        var templates = await db.ShiftTemplates.AsNoTracking().ToDictionaryAsync(t => t.Id, ct);
        return new ScheduleSnapshot(await assignments.ToListAsync(ct), await overrides.ToListAsync(ct), await leaves.ToListAsync(ct), templates);
    }

    public async Task<List<Guid>> ActiveCollectorIdsAsync(CancellationToken ct) =>
        await db.Users.Where(u => u.IsActive && u.Role == UserRole.Collector).Select(u => u.Id).ToListAsync(ct);
}

/// <summary>
/// Spec 3.11: keeps AttendanceRecords in sync with the schedule for today onwards.
/// Idempotent. Records with a check-in are never touched; planned rows without one are derived data
/// and may be removed when the schedule no longer produces them.
/// </summary>
public sealed class MaterializationService(AppDbContext db, ScheduleSnapshotLoader loader, IClock clock)
{
    public async Task SyncAsync(IReadOnlyCollection<Guid>? employeeIds, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var today = clock.Today;
        if (from < today) from = today;
        if (to < from) return;

        var employees = employeeIds?.ToList() ?? await loader.ActiveCollectorIdsAsync(ct);
        if (employees.Count == 0) return;

        var snapshot = await loader.LoadAsync(employees, from, to, ct);
        var existing = await db.AttendanceRecords
            .Where(r => employees.Contains(r.EmployeeId) && r.ShiftDate >= from && r.ShiftDate <= to)
            .ToListAsync(ct);

        foreach (var employeeId in employees)
        {
            for (var date = from; date <= to; date = date.AddDays(1))
            {
                var current = existing.Where(r => r.EmployeeId == employeeId && r.ShiftDate == date).ToList();
                foreach (var shift in ScheduleResolver.Resolve(employeeId, date, snapshot))
                {
                    var match = current.FirstOrDefault(r => r.ScheduledStart == shift.Window.Start);
                    if (match is null)
                    {
                        db.AttendanceRecords.Add(new AttendanceRecord(shift));
                        continue;
                    }
                    current.Remove(match);
                    if (!match.HasCheckedIn && NeedsUpdate(match, shift))
                        match.ApplySchedule(shift);
                }

                db.AttendanceRecords.RemoveRange(current.Where(r => !r.HasCheckedIn));
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private static bool NeedsUpdate(AttendanceRecord r, ResolvedShift s) =>
        r.LocationId != s.LocationId || r.ShiftTemplateId != s.ShiftTemplateId || r.ScheduledEnd != s.Window.End
        || r.BreakMinutes != s.BreakMinutes || r.GraceMinutes != s.GraceMinutes
        || r.CountEarlyArrivalAsOvertime != s.CountEarlyArrivalAsOvertime
        || (r.Status == AttendanceStatus.OnLeave) != s.IsOnLeave;
}

/// <summary>The single entry point for recomputing a day (spec 3.8). Caller must Include(r => r.Exits).</summary>
public sealed class RecalculationService(AppDbContext db, IOptions<AttendanceOptions> options)
{
    public async Task RecalculateAsync(AttendanceRecord record, CancellationToken ct)
    {
        var approved = await db.PermissionRequests.AsNoTracking()
            .Where(p => p.EmployeeId == record.EmployeeId && p.ShiftDate == record.ShiftDate
                        && p.IsActive && p.Status == RequestStatus.Approved)
            .ToListAsync(ct);

        var window = record.ScheduledWindow;
        var late = new List<TimeInterval>();
        var early = new List<TimeInterval>();
        foreach (var p in approved)
        {
            // An employee with two shifts that day: a permission applies only to the shift it fits in.
            if (TryInterval(p, window) is not { } interval) continue;
            if (p.Type == PermissionType.Late) late.Add(interval);
            else if (p.Type == PermissionType.EarlyDeparture) early.Add(interval);
        }

        var input = record.BuildCalculationInput(late, early, options.Value.MinimumOvertimeMinutes);
        record.ApplyCalculation(AttendanceCalculator.Calculate(input));
    }

    public async Task RecalculateForAsync(Guid employeeId, DateOnly shiftDate, CancellationToken ct)
    {
        var records = await db.AttendanceRecords.Include(r => r.Exits)
            .Where(r => r.EmployeeId == employeeId && r.ShiftDate == shiftDate && r.CheckInAt != null)
            .ToListAsync(ct);
        foreach (var r in records) await RecalculateAsync(r, ct);
    }

    internal static TimeInterval? TryInterval(PermissionRequest p, TimeInterval window)
    {
        try { return p.ToInterval(window); }
        catch (DomainException) { return null; }
    }
}

/// <summary>Time-driven rules: auto check-out and absence (spec 3.4, 3.7). Idempotent.</summary>
public sealed class AttendanceJobs(AppDbContext db, MaterializationService materializer, RecalculationService recalculator,
    IClock clock, IOptions<AttendanceOptions> options)
{
    public Task MaterializeAsync(CancellationToken ct) =>
        materializer.SyncAsync(null, clock.Today, clock.Today.AddDays(1), ct);

    public async Task CloseAndMarkAsync(CancellationToken ct)
    {
        var now = clock.Now;
        var since = clock.Today.AddDays(-2);
        var closeBefore = now.AddMinutes(-options.Value.AutoCloseDelayMinutes);

        var toClose = await db.AttendanceRecords.Include(r => r.Exits)
            .Where(r => r.ShiftDate >= since && r.CheckInAt != null && r.CheckOutAt == null && r.ScheduledEnd <= closeBefore)
            .ToListAsync(ct);
        foreach (var r in toClose)
        {
            if (r.AutoClose(now, options.Value.AutoCloseDelayMinutes))
                await recalculator.RecalculateAsync(r, ct);
        }

        var pendingCheckIns = await db.AttendanceExceptionRequests
            .Where(e => e.Status == RequestStatus.Pending && e.Kind == ExceptionKind.CheckIn)
            .Select(e => e.AttendanceRecordId).ToListAsync(ct);
        var pending = pendingCheckIns.ToHashSet();

        var toMark = await db.AttendanceRecords
            .Where(r => r.ShiftDate >= since && r.Status == AttendanceStatus.Scheduled && r.CheckInAt == null && r.ScheduledEnd <= now)
            .ToListAsync(ct);
        foreach (var r in toMark)
            r.MarkAbsentIfDue(now, pending.Contains(r.Id));

        await db.SaveChangesAsync(ct);
    }
}

public sealed class AttendanceWorker(IServiceScopeFactory scopes, ILogger<AttendanceWorker> logger) : BackgroundService
{
    private const int MaterializeEveryMinutes = 30;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        var tick = 0;
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var jobs = scope.ServiceProvider.GetRequiredService<AttendanceJobs>();
                if (tick % MaterializeEveryMinutes == 0) await jobs.MaterializeAsync(stoppingToken);
                await jobs.CloseAndMarkAsync(stoppingToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // A user changed a record mid-run; the next tick picks it up.
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Attendance job run failed");
            }
            tick++;
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
