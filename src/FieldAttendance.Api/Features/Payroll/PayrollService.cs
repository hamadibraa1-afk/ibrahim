using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Api.Features.Discipline;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Payroll;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Payroll;

public sealed record PayrollBlocker(string Code, string Detail, int Count);

/// <summary>
/// Builds a month of payslips from data that already exists: attendance, approved deductions,
/// allowances and unpaid leave. Nothing is invented here, and nothing is taken from an
/// employee without an approved decision behind it.
/// </summary>
public sealed class PayrollService(AppDbContext db, DeductionService deductions, SalaryLookup salaries, IClock clock)
{
    public async Task<PayrollCycle> OpenAsync(int year, int month, Guid userId, CancellationToken ct)
    {
        if (await db.PayrollCycles.AnyAsync(c => c.Year == year && c.Month == month, ct))
            throw new DomainException("payroll.exists", "A cycle already exists for this month.");

        var policy = await deductions.PolicyAsync(ct);
        var cycle = new PayrollCycle(year, month, policy.MonthDays, policy.DailyWorkMinutes,
            policy.OvertimeFactor, policy.MaxDeductionPercent, userId);
        db.PayrollCycles.Add(cycle);
        await db.SaveChangesAsync(ct);
        return cycle;
    }

    /// <summary>
    /// Things that must be settled before the month can be approved: undecided deductions and
    /// leave requests still waiting, because both change the numbers after the fact.
    /// </summary>
    public async Task<IReadOnlyList<PayrollBlocker>> BlockersAsync(PayrollCycle cycle, CancellationToken ct)
    {
        var pendingDeductions = await db.DeductionProposals.CountAsync(p => p.IsActive
            && p.Status == DeductionStatus.Proposed && p.OnDate >= cycle.FirstDay && p.OnDate <= cycle.LastDay, ct);
        var pendingLeaves = await db.LeaveRequests.CountAsync(l => l.IsActive
            && l.Status == RequestStatus.Pending && l.FromDate <= cycle.LastDay && l.ToDate >= cycle.FirstDay, ct);
        var pendingPermissions = await db.PermissionRequests.CountAsync(p => p.IsActive
            && p.Status == RequestStatus.Pending && p.ShiftDate >= cycle.FirstDay && p.ShiftDate <= cycle.LastDay, ct);

        var blockers = new List<PayrollBlocker>();
        if (pendingDeductions > 0) blockers.Add(new PayrollBlocker("payroll.pending_deductions", "", pendingDeductions));
        if (pendingLeaves > 0) blockers.Add(new PayrollBlocker("payroll.pending_leaves", "", pendingLeaves));
        if (pendingPermissions > 0) blockers.Add(new PayrollBlocker("payroll.pending_permissions", "", pendingPermissions));
        return blockers;
    }

    /// <summary>Recalculates every payslip in the cycle from scratch, so a rerun never doubles anything.</summary>
    public async Task CalculateAsync(PayrollCycle cycle, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        cycle.EnsureOpen();

        var policy = new PayrollPolicy(cycle.MonthDays, cycle.DailyWorkMinutes, cycle.OvertimeFactor, cycle.MaxDeductionPercent);
        var profiles = await db.EmployeeProfiles.AsNoTracking()
            .Where(p => p.Status != EmploymentStatus.Ended || (p.EndDate != null && p.EndDate >= cycle.FirstDay))
            .ToListAsync(ct);
        var employeeIds = profiles.Select(p => p.UserId).ToList();

        var attendance = await db.AttendanceRecords.AsNoTracking()
            .Where(r => employeeIds.Contains(r.EmployeeId) && r.ShiftDate >= cycle.FirstDay && r.ShiftDate <= cycle.LastDay)
            .ToListAsync(ct);
        var approvedDeductions = await db.DeductionProposals
            .Where(p => p.IsActive && p.Status == DeductionStatus.Approved
                        && p.OnDate >= cycle.FirstDay && p.OnDate <= cycle.LastDay)
            .ToListAsync(ct);
        var deductionTypes = await db.DeductionTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, ct);
        var allowances = await db.EmployeeAllowances.AsNoTracking()
            .Where(a => a.IsActive && a.FromDate <= cycle.LastDay && a.ToDate >= cycle.FirstDay).ToListAsync(ct);
        var allowanceTypes = await db.AllowanceTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, ct);
        var unpaidLeaves = await UnpaidLeaveDaysAsync(cycle, employeeIds, ct);
        var salarySteps = await salaries.StepsAsync(employeeIds, ct);
        var monthly = await db.SalaryAllowances.AsNoTracking()
            .Where(a => a.IsActive && employeeIds.Contains(a.EmployeeId) && a.FromDate <= cycle.LastDay
                        && (a.ToDate == null || a.ToDate >= cycle.FirstDay))
            .OrderBy(a => a.FromDate).ToListAsync(ct);

        var existing = await db.PayrollLines.Include(l => l.Items)
            .Where(l => l.PayrollCycleId == cycle.Id).ToListAsync(ct);
        db.PayrollLines.RemoveRange(existing);
        foreach (var proposal in approvedDeductions.Where(p => p.PayrollCycleId == cycle.Id)) proposal.DetachFromPayroll();

        foreach (var profile in profiles)
        {
            var days = attendance.Where(r => r.EmployeeId == profile.UserId).ToList();
            // The month's basic comes from salary history: a raise dated next month is not paid now,
            // and one starting mid-month is paid for the days it covers.
            var basic = SalaryTimeline.ForPeriod(salarySteps.GetValueOrDefault(profile.UserId, []),
                cycle.FirstDay, cycle.LastDay, profile.BasicSalary);
            var line = new PayrollLine(cycle.Id, profile.UserId, basic);

            var unpaidDays = unpaidLeaves.GetValueOrDefault(profile.UserId);
            line.SetAttendance(
                scheduledDays: days.Count,
                presentDays: days.Count(r => r.CheckInAt != null),
                absentDays: days.Count(r => r.Status == AttendanceStatus.Absent),
                leaveDays: days.Count(r => r.Status == AttendanceStatus.OnLeave),
                unpaidLeaveDays: unpaidDays,
                lateMinutes: days.Sum(r => r.LateUnexcused),
                overtimeMinutes: days.Sum(r => r.OvertimeMinutes));

            line.AddItem("الراتب الأساسي", basic, isDeduction: false, "salary");

            foreach (var allowance in allowances.Where(a => a.EmployeeId == profile.UserId))
            {
                if (!allowanceTypes.TryGetValue(allowance.AllowanceTypeId, out var type) || type.DailyAmount is not { } daily) continue;
                var covered = CoveredDays(allowance.FromDate, allowance.ToDate, cycle.FirstDay, cycle.LastDay);
                if (covered > 0) line.AddItem(type.NameAr, daily * covered, false, $"allowance:{allowance.Id}");
            }

            // Fixed monthly allowances: separate lines, outside the basic, so the deduction
            // ceiling and every deduction stay priced from the basic alone.
            foreach (var allowance in monthly.Where(a => a.EmployeeId == profile.UserId))
            {
                var amount = allowance.AmountFor(cycle.FirstDay, cycle.LastDay);
                if (amount > 0) line.AddItem(allowance.Name, amount, false, $"monthly:{allowance.Id}");
            }

            var overtime = PayrollMath.OvertimeAmount(basic, policy, line.OvertimeMinutes);
            if (overtime > 0) line.AddItem("العمل الإضافي", overtime, false, "overtime");

            if (unpaidDays > 0)
                line.AddItem("إجازة بدون راتب", PayrollMath.UnpaidLeaveAmount(basic, policy, unpaidDays), true, "unpaid");

            foreach (var proposal in approvedDeductions.Where(p => p.EmployeeId == profile.UserId))
            {
                var label = deductionTypes.GetValueOrDefault(proposal.DeductionTypeId)?.NameAr ?? "خصم";
                line.AddItem($"{label} — {proposal.OnDate:yyyy-MM-dd}", proposal.ApprovedAmount ?? 0m, true, $"deduction:{proposal.Id}");
                proposal.AttachToPayroll(cycle.Id);
            }

            var requested = line.Items.Where(i => i.IsDeduction && i.SourceKey != "unpaid").Sum(i => i.Amount);
            var unpaidAmount = line.Items.Where(i => i.SourceKey == "unpaid").Sum(i => i.Amount);
            // Unpaid leave is absence of work, not a penalty, so the disciplinary ceiling applies to the rest.
            var capped = PayrollMath.CapDeductions(basic, policy, requested) + unpaidAmount;
            line.Total(capped);

            db.PayrollLines.Add(line);
        }

        cycle.MarkCalculated(clock.Now);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Days of approved, unpaid leave that fall inside the month.</summary>
    private async Task<Dictionary<Guid, int>> UnpaidLeaveDaysAsync(PayrollCycle cycle, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct)
    {
        var unpaidTypes = await db.LeaveTypes.AsNoTracking()
            .Where(t => t.NameEn.Contains("Unpaid") || t.NameAr.Contains("بدون راتب"))
            .Select(t => t.Id).ToListAsync(ct);
        if (unpaidTypes.Count == 0) return [];

        var leaves = await db.LeaveRequests.AsNoTracking()
            .Where(l => l.IsActive && l.Status == RequestStatus.Approved && unpaidTypes.Contains(l.LeaveTypeId)
                        && employeeIds.Contains(l.EmployeeId) && l.FromDate <= cycle.LastDay && l.ToDate >= cycle.FirstDay)
            .ToListAsync(ct);

        return leaves.GroupBy(l => l.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Sum(l => CoveredDays(l.FromDate, l.ToDate, cycle.FirstDay, cycle.LastDay)));
    }

    private static int CoveredDays(DateOnly from, DateOnly to, DateOnly windowStart, DateOnly windowEnd)
    {
        var start = from > windowStart ? from : windowStart;
        var end = to < windowEnd ? to : windowEnd;
        return end < start ? 0 : end.DayNumber - start.DayNumber + 1;
    }
}
