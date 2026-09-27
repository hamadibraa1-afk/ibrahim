using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Api.Features.Attendance;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Scheduling;
using FieldAttendance.Domain.Time;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Requests;

public sealed record SubmitPermissionRequest(string Type, DateOnly ShiftDate, TimeOnly? FromTime, TimeOnly? ToTime, string Reason);

public sealed class PermissionService(AppDbContext db, ScheduleSnapshotLoader loader, RecalculationService recalculator, IClock clock,
    Approvals.ApprovalService approvals)
{
    public async Task<PermissionRequest> SubmitAsync(Guid employeeId, Guid submittedBy, SubmitPermissionRequest r, CancellationToken ct)
    {
        if (r.ShiftDate < clock.Today.AddDays(-1))
            throw new DomainException("permission.too_old", "Permissions can be requested for yesterday, today or a future date.");
        if (!Enum.TryParse<PermissionType>(r.Type, true, out var type))
            throw new DomainException("permission.invalid_type", "Unknown permission type.");

        var windows = await ShiftWindowsAsync(employeeId, r.ShiftDate, ct);
        if (windows.Count == 0)
            throw new DomainException("permission.no_shift", "You have no shift on this date.");

        PermissionRequest? request = null;
        DomainException? lastError = null;
        foreach (var window in windows)
        {
            try
            {
                request = type switch
                {
                    PermissionType.Late => PermissionRequest.Late(employeeId, r.ShiftDate, Required(r.ToTime), window, r.Reason, submittedBy),
                    PermissionType.TemporaryExit => PermissionRequest.TemporaryExit(employeeId, r.ShiftDate, Required(r.FromTime), Required(r.ToTime), window, r.Reason, submittedBy),
                    _ => PermissionRequest.EarlyDeparture(employeeId, r.ShiftDate, Required(r.FromTime), window, r.Reason, submittedBy),
                };
                await EnsureNoOverlapAsync(request, window, ct);
                break;
            }
            catch (DomainException ex)
            {
                lastError = ex;
                request = null;
            }
        }

        if (request is null) throw lastError!;
        db.PermissionRequests.Add(request);
        await db.SaveChangesAsync(ct);

        // Same routing as leave: the chain for the employee's workforce decides who signs.
        await approvals.StartAsync(RequestKind.Permission, request.Id, employeeId, ct);
        return request;
    }

    /// <summary>
    /// One signature. Only the person the permission is currently waiting on may sign, and it is
    /// approved only when the last level signs.
    /// </summary>
    public async Task DecideAsync(Guid id, Guid supervisorId, bool approve, string? rejectReason, CancellationToken ct)
    {
        var p = await db.PermissionRequests.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new DomainException("request.not_found", "Request not found.");

        if (!await approvals.HasChainAsync(RequestKind.Permission, id, ct))
            await approvals.StartAsync(RequestKind.Permission, id, p.EmployeeId, ct);
        var outcome = await approvals.DecideAsync(RequestKind.Permission, id, supervisorId, approve,
            approve ? null : rejectReason, ct);
        if (outcome == RequestStatus.Pending) return; // still travelling up the chain

        if (approve) p.Approve(supervisorId, clock.Now);
        else p.Reject(supervisorId, clock.Now, rejectReason ?? string.Empty);
        await db.SaveChangesAsync(ct);
        if (approve) await RecalculateAsync(p, ct); // retroactive approvals recompute the day (spec 3.5)
    }

    public async Task CancelAsync(Guid id, Guid employeeId, CancellationToken ct)
    {
        var p = await db.PermissionRequests.SingleOrDefaultAsync(x => x.Id == id && x.EmployeeId == employeeId, ct)
            ?? throw new DomainException("request.not_found", "Request not found.");
        var windows = await ShiftWindowsAsync(employeeId, p.ShiftDate, ct);
        var window = windows.FirstOrDefault(w => RecalculationService.TryInterval(p, w) is not null);
        var wasApproved = p.Status == RequestStatus.Approved;
        p.Cancel(clock.Now, window);
        await db.SaveChangesAsync(ct);
        if (wasApproved) await RecalculateAsync(p, ct);
    }

    private async Task RecalculateAsync(PermissionRequest p, CancellationToken ct)
    {
        await recalculator.RecalculateForAsync(p.EmployeeId, p.ShiftDate, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Materialized records when they exist (they hold the real snapshot); otherwise the resolved schedule.</summary>
    private async Task<List<TimeInterval>> ShiftWindowsAsync(Guid employeeId, DateOnly date, CancellationToken ct)
    {
        var records = await db.AttendanceRecords.AsNoTracking()
            .Where(r => r.EmployeeId == employeeId && r.ShiftDate == date && r.Status != AttendanceStatus.OnLeave)
            .OrderBy(r => r.ScheduledStart).ToListAsync(ct);
        if (records.Count > 0) return records.Select(r => r.ScheduledWindow).ToList();

        var snapshot = await loader.LoadAsync([employeeId], date, date, ct);
        return ScheduleResolver.Resolve(employeeId, date, snapshot).Where(s => !s.IsOnLeave).Select(s => s.Window).ToList();
    }

    private async Task EnsureNoOverlapAsync(PermissionRequest candidate, TimeInterval window, CancellationToken ct)
    {
        var mine = candidate.ToInterval(window);
        var others = await db.PermissionRequests.AsNoTracking()
            .Where(p => p.EmployeeId == candidate.EmployeeId && p.ShiftDate == candidate.ShiftDate && p.IsActive
                        && (p.Status == RequestStatus.Pending || p.Status == RequestStatus.Approved))
            .ToListAsync(ct);
        if (others.Any(o => RecalculationService.TryInterval(o, window) is { } i && i.Overlaps(mine)))
            throw new DomainException("permission.overlap", "This overlaps another pending or approved permission.");
    }

    private static TimeOnly Required(TimeOnly? t) =>
        t ?? throw new DomainException("permission.time_required", "Please enter the permission time.");
}
