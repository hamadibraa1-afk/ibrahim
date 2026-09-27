using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Api.Features.Approvals;
using FieldAttendance.Api.Features.Attendance;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Leaves;

public sealed record SubmitLeaveRequest(Guid LeaveTypeId, DateOnly FromDate, DateOnly ToDate, string? Reason);

public sealed record LeaveDto(Guid Id, Guid EmployeeId, string? EmployeeName, Guid LeaveTypeId, string LeaveTypeName,
    DateOnly FromDate, DateOnly ToDate, int WorkingDays, string Status, string? Reason, string? RejectReason,
    string? DecidedByName, DateTimeOffset CreatedAt, Guid? DecidedById = null,
    bool IsPaid = true, bool RequiresAttachment = false, int AttachmentCount = 0);

public sealed record LeaveBalanceDto(Guid LeaveTypeId, string LeaveTypeName, int Year, int? TotalDays, int UsedDays, int? RemainingDays,
    bool IsPaid = true, bool RequiresAttachment = false);

/// <summary>
/// Leave affects two things: the employee's balance and the schedule.
/// Approving deducts only the days the employee was actually scheduled to work (spec 3.6),
/// then re-syncs attendance records so those days show as leave and the location shows uncovered.
/// </summary>
public sealed class LeaveService(AppDbContext db, ScheduleSnapshotLoader loader, MaterializationService materializer,
    ApprovalService approvals, PayrollLock payrollLock, IClock clock)
{
    public async Task<LeaveRequest> SubmitAsync(Guid employeeId, Guid submittedBy, SubmitLeaveRequest r, CancellationToken ct)
    {
        if (r.ToDate < r.FromDate)
            throw new DomainException("leave.end_before_start", "End date cannot be before start date.");
        if (r.ToDate < clock.Today.AddDays(-1))
            throw new DomainException("leave.too_old", "Leave cannot be requested for a period that already passed.");
        if (!await db.LeaveTypes.AnyAsync(t => t.Id == r.LeaveTypeId && t.IsActive, ct))
            throw new DomainException("leave.type_not_found", "Leave type not found.");

        var overlaps = await db.LeaveRequests.AnyAsync(l => l.EmployeeId == employeeId && l.IsActive
            && (l.Status == RequestStatus.Pending || l.Status == RequestStatus.Approved)
            && l.FromDate <= r.ToDate && l.ToDate >= r.FromDate, ct);
        if (overlaps)
            throw new DomainException("leave.overlap", "This overlaps another leave request.");

        var snapshot = await loader.LoadAsync([employeeId], r.FromDate, r.ToDate, ct);
        var days = ScheduleResolver.CountScheduledWorkingDays(employeeId, r.FromDate, r.ToDate, snapshot);

        var leave = new LeaveRequest(employeeId, r.LeaveTypeId, r.FromDate, r.ToDate, days, r.Reason, submittedBy);
        db.LeaveRequests.Add(leave);
        await db.SaveChangesAsync(ct);

        // The chain HR configured decides who signs; an empty chain means HR alone.
        await approvals.StartAsync(RequestKind.Leave, leave.Id, employeeId, ct);
        return leave;
    }

    /// <summary>
    /// One signature on the leave. The leave only becomes approved when the last level signs,
    /// and only then is the balance deducted and the schedule updated.
    /// </summary>
    public async Task DecideAsync(Guid id, Guid supervisorId, bool approve, string? rejectReason, CancellationToken ct)
    {
        var leave = await Find(id, ct);
        await payrollLock.EnsureRangeOpenAsync(leave.FromDate, leave.ToDate, ct);
        if (approve) await EnsureNoAttendanceConflictAsync(leave, ct);
        if (approve) await EnsureRequiredDocumentAsync(leave, ct);
        // A pending leave without a chain (filed before chains existed) gets one now, so the same rule
        // holds for every leave: only the person it is waiting on may sign.
        if (!await approvals.HasChainAsync(RequestKind.Leave, id, ct))
            await approvals.StartAsync(RequestKind.Leave, id, leave.EmployeeId, ct);
        var outcome = await approvals.DecideAsync(RequestKind.Leave, id, supervisorId, approve,
            approve ? null : rejectReason, ct);
        if (outcome == RequestStatus.Pending) return; // still travelling up the chain

        if (approve)
        {
            leave.Approve(supervisorId, clock.Now);
            var balance = await EnsureBalanceAsync(leave, ct);
            balance?.Deduct(leave.WorkingDays);
        }
        else
        {
            leave.Reject(supervisorId, clock.Now, rejectReason ?? string.Empty);
        }

        if (approve && !await db.ReturnsToWork.AnyAsync(r => r.LeaveRequestId == leave.Id, ct))
            db.ReturnsToWork.Add(new ReturnToWork(leave.EmployeeId, leave.Id, leave.ToDate.AddDays(1)));

        await db.SaveChangesAsync(ct);
        if (approve) await materializer.SyncAsync([leave.EmployeeId], leave.FromDate, leave.ToDate, ct);
    }

    /// <summary>
    /// A type that needs a document (a medical report for sick leave) cannot be approved at any
    /// level until one is attached; each approver signs having seen it.
    /// </summary>
    private async Task EnsureRequiredDocumentAsync(LeaveRequest leave, CancellationToken ct)
    {
        var required = await db.LeaveTypes.AsNoTracking().Where(t => t.Id == leave.LeaveTypeId).Select(t => t.RequiresAttachment).SingleAsync(ct);
        if (required && !await db.LeaveAttachments.AnyAsync(a => a.LeaveRequestId == leave.Id && a.IsActive, ct))
            throw new DomainException("leave.attachment_required", "This leave type needs a supporting document before it can be approved.");
    }

    /// <summary>
    /// An approved leave must not land on a day the employee actually worked: that would
    /// erase a real attendance record and change hours already counted.
    /// </summary>
    private async Task EnsureNoAttendanceConflictAsync(LeaveRequest leave, CancellationToken ct)
    {
        var worked = await db.AttendanceRecords.AsNoTracking()
            .Where(r => r.EmployeeId == leave.EmployeeId && r.ShiftDate >= leave.FromDate && r.ShiftDate <= leave.ToDate
                        && r.CheckInAt != null)
            .Select(r => r.ShiftDate).ToListAsync(ct);
        if (worked.Count > 0)
            throw new DomainException("leave.attendance_conflict",
                $"The employee already has attendance on {worked.Count} day(s) in this period.");
    }

    /// <summary>
    /// Where the request stands right now, for the employee to follow. The owner check matters:
    /// the timeline carries approver names and their notes, and the id arrives from the URL.
    /// </summary>
    public async Task<IReadOnlyList<ApprovalStepView>> TimelineAsync(Guid id, Guid? employeeId, CancellationToken ct)
    {
        var leave = await Find(id, ct);
        if (employeeId is { } owner && leave.EmployeeId != owner)
            throw new DomainException("request.not_found", "Request not found.");
        return await approvals.TimelineAsync(RequestKind.Leave, id, ct);
    }

    public async Task CancelAsync(Guid id, Guid? employeeId, CancellationToken ct)
    {
        var leave = await Find(id, ct);
        if (employeeId is { } me && leave.EmployeeId != me)
            throw new DomainException("request.not_found", "Request not found.");

        await payrollLock.EnsureRangeOpenAsync(leave.FromDate, leave.ToDate, ct);
        var wasApproved = leave.Status == RequestStatus.Approved;
        leave.Cancel();
        if (wasApproved)
        {
            var balance = await EnsureBalanceAsync(leave, ct);
            balance?.Restore(leave.WorkingDays);
        }

        await db.SaveChangesAsync(ct);
        if (wasApproved) await materializer.SyncAsync([leave.EmployeeId], leave.FromDate, leave.ToDate, ct);
    }

    public async Task<IReadOnlyList<LeaveBalanceDto>> BalancesAsync(Guid employeeId, CancellationToken ct)
    {
        var year = clock.Today.Year;
        var types = await db.LeaveTypes.AsNoTracking().Where(t => t.IsActive).ToListAsync(ct);
        var balances = await db.LeaveBalances.AsNoTracking()
            .Where(b => b.EmployeeId == employeeId && b.Year == year).ToListAsync(ct);

        return types.Select(t =>
        {
            var b = balances.FirstOrDefault(x => x.LeaveTypeId == t.Id);
            var total = b?.TotalDays ?? t.AnnualBalanceDays;
            var used = b?.UsedDays ?? 0;
            return new LeaveBalanceDto(t.Id, t.NameAr, year, total, used, total - used, t.IsPaid, t.RequiresAttachment);
        }).ToList();
    }

    public async Task<IReadOnlyList<LeaveDto>> ListAsync(Guid? employeeId, RequestStatus? status, CancellationToken ct)
    {
        var query = db.LeaveRequests.AsNoTracking().Where(l => l.IsActive);
        if (employeeId is { } e) query = query.Where(l => l.EmployeeId == e);
        if (status is { } s) query = query.Where(l => l.Status == s);

        var list = await query.OrderByDescending(l => l.CreatedAt).Take(500).ToListAsync(ct);
        var types = await db.LeaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, ct);
        var ids = list.Select(l => l.EmployeeId).Concat(list.Where(l => l.DecidedBy != null).Select(l => l.DecidedBy!.Value)).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var leaveIds = list.Select(l => l.Id).ToList();
        var files = await db.LeaveAttachments.AsNoTracking().Where(f => f.IsActive && leaveIds.Contains(f.LeaveRequestId))
            .GroupBy(f => f.LeaveRequestId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return list.Select(l => new LeaveDto(l.Id, l.EmployeeId, names.GetValueOrDefault(l.EmployeeId),
            l.LeaveTypeId, types.GetValueOrDefault(l.LeaveTypeId)?.NameAr ?? "?", l.FromDate, l.ToDate, l.WorkingDays,
            l.Status.ToString(), l.Reason, l.RejectReason,
            l.DecidedBy is { } d ? names.GetValueOrDefault(d) : null, l.CreatedAt, l.DecidedBy,
            types.GetValueOrDefault(l.LeaveTypeId)?.IsPaid ?? true, types.GetValueOrDefault(l.LeaveTypeId)?.RequiresAttachment ?? false,
            files.GetValueOrDefault(l.Id))).ToList();
    }

    /// <summary>Balance rows are created on first use from the type's annual allowance; unlimited types have none.</summary>
    private async Task<LeaveBalance?> EnsureBalanceAsync(LeaveRequest leave, CancellationToken ct)
    {
        var type = await db.LeaveTypes.AsNoTracking().SingleAsync(t => t.Id == leave.LeaveTypeId, ct);
        if (type.AnnualBalanceDays is null) return null;

        var year = leave.FromDate.Year;
        var balance = await db.LeaveBalances
            .SingleOrDefaultAsync(b => b.EmployeeId == leave.EmployeeId && b.LeaveTypeId == leave.LeaveTypeId && b.Year == year, ct);
        if (balance is null)
        {
            balance = new LeaveBalance(leave.EmployeeId, leave.LeaveTypeId, year, type.AnnualBalanceDays);
            db.LeaveBalances.Add(balance);
        }
        return balance;
    }

    private async Task<LeaveRequest> Find(Guid id, CancellationToken ct) =>
        await db.LeaveRequests.SingleOrDefaultAsync(l => l.Id == id && l.IsActive, ct)
        ?? throw new DomainException("request.not_found", "Request not found.");
}
