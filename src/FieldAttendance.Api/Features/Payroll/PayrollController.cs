using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Payroll;

public sealed record CycleDto(Guid Id, int Year, int Month, string Status, int MonthDays, decimal MaxDeductionPercent,
    DateTimeOffset? CalculatedAt, DateTimeOffset? ApprovedAt, DateTimeOffset? ClosedAt, int Lines, decimal TotalNet);

public sealed record PayslipItemDto(string Label, decimal Amount, bool IsDeduction);

public sealed record PayslipDto(Guid Id, Guid EmployeeId, string EmployeeName, string? EmployeeNumber, string? DepartmentName,
    decimal BasicSalary, int ScheduledDays, int PresentDays, int AbsentDays, int LeaveDays, int UnpaidLeaveDays,
    int LateMinutes, int OvertimeMinutes, decimal Earnings, decimal Deductions, decimal CappedDeductions, decimal NetPay,
    IReadOnlyList<PayslipItemDto> Items);

public sealed record OpenCycleRequest([Range(2020, 2100)] int Year, [Range(1, 12)] int Month);

/// <summary>
/// The monthly payroll run. Numbers come from attendance and approved decisions only;
/// approving freezes them, and closing makes the month permanent.
/// </summary>
[ApiController]
[Route("api/hr/payroll")]
[Authorize(Policy = HrPolicies.Manage)]
public sealed class PayrollController(AppDbContext db, PayrollService payroll, ICurrentUser me, IClock clock) : ControllerBase
{
    [HttpGet("cycles")]
    public async Task<IReadOnlyList<CycleDto>> Cycles(CancellationToken ct)
    {
        var cycles = await db.PayrollCycles.AsNoTracking().OrderByDescending(c => c.Year).ThenByDescending(c => c.Month).ToListAsync(ct);
        var totals = await db.PayrollLines.AsNoTracking()
            .GroupBy(l => l.PayrollCycleId)
            .Select(g => new { g.Key, Count = g.Count(), Net = g.Sum(l => l.NetPay) }).ToListAsync(ct);

        return cycles.Select(c =>
        {
            var total = totals.FirstOrDefault(t => t.Key == c.Id);
            return new CycleDto(c.Id, c.Year, c.Month, c.Status.ToString(), c.MonthDays, c.MaxDeductionPercent,
                c.CalculatedAt, c.ApprovedAt, c.ClosedAt, total?.Count ?? 0, total?.Net ?? 0m);
        }).ToList();
    }

    [HttpPost("cycles")]
    public async Task<ActionResult<CycleDto>> Open(OpenCycleRequest r, CancellationToken ct)
    {
        var cycle = await payroll.OpenAsync(r.Year, r.Month, me.RequiredId, ct);
        return new CycleDto(cycle.Id, cycle.Year, cycle.Month, cycle.Status.ToString(), cycle.MonthDays,
            cycle.MaxDeductionPercent, null, null, null, 0, 0m);
    }

    [HttpGet("cycles/{id:guid}/blockers")]
    public async Task<IReadOnlyList<PayrollBlocker>> Blockers(Guid id, CancellationToken ct) =>
        await payroll.BlockersAsync(await Find(id, ct), ct);

    [HttpPost("cycles/{id:guid}/calculate")]
    public async Task<IActionResult> Calculate(Guid id, CancellationToken ct)
    {
        await payroll.CalculateAsync(await Find(id, ct), ct);
        return NoContent();
    }

    [HttpGet("cycles/{id:guid}/payslips")]
    public async Task<IReadOnlyList<PayslipDto>> Payslips(Guid id, CancellationToken ct)
    {
        var cycle = await Find(id, ct);
        var lines = await db.PayrollLines.AsNoTracking().Include(l => l.Items)
            .Where(l => l.PayrollCycleId == cycle.Id).ToListAsync(ct);
        return await ToDtosAsync(lines, ct);
    }

    /// <summary>Approval is refused while anything that would change the numbers is still undecided.</summary>
    [HttpPost("cycles/{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var cycle = await Find(id, ct);
        var blockers = await payroll.BlockersAsync(cycle, ct);
        if (blockers.Count > 0)
            throw new DomainException(blockers[0].Code, "Settle the pending items before approving the cycle.");

        cycle.Approve(me.RequiredId, clock.Now);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("cycles/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct)
    {
        var cycle = await Find(id, ct);
        cycle.Close(clock.Now);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("cycles/{id:guid}/reopen")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> Reopen(Guid id, CancellationToken ct)
    {
        var cycle = await Find(id, ct);
        cycle.Reopen();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>The payroll report finance receives: one row per employee, UTF-8 with a BOM for Excel.</summary>
    [HttpGet("cycles/{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id, CancellationToken ct)
    {
        var cycle = await Find(id, ct);
        var lines = await db.PayrollLines.AsNoTracking().Include(l => l.Items)
            .Where(l => l.PayrollCycleId == cycle.Id).ToListAsync(ct);
        var slips = await ToDtosAsync(lines, ct);

        var csv = new StringBuilder();
        csv.AppendLine(string.Join(',', "الرقم الوظيفي", "الاسم", "الإدارة", "الراتب الأساسي", "أيام الدوام",
            "أيام الحضور", "أيام الغياب", "أيام الإجازة", "إجازة بدون راتب", "دقائق التأخير", "دقائق العمل الإضافي",
            "إجمالي المستحقات", "إجمالي الخصومات", "الخصم المطبّق", "صافي الراتب"));

        foreach (var s in slips.OrderBy(s => s.EmployeeNumber, StringComparer.Ordinal))
        {
            csv.AppendLine(string.Join(',', Escape(s.EmployeeNumber), Escape(s.EmployeeName), Escape(s.DepartmentName),
                Money(s.BasicSalary), s.ScheduledDays, s.PresentDays, s.AbsentDays, s.LeaveDays, s.UnpaidLeaveDays,
                s.LateMinutes, s.OvertimeMinutes, Money(s.Earnings), Money(s.Deductions), Money(s.CappedDeductions), Money(s.NetPay)));
        }

        csv.AppendLine(string.Join(',', "", "الإجمالي", "", "", "", "", "", "", "", "", "",
            Money(slips.Sum(s => s.Earnings)), Money(slips.Sum(s => s.Deductions)),
            Money(slips.Sum(s => s.CappedDeductions)), Money(slips.Sum(s => s.NetPay))));

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv", $"payroll_{cycle.Year}-{cycle.Month:D2}.csv");
    }

    private async Task<IReadOnlyList<PayslipDto>> ToDtosAsync(IReadOnlyCollection<PayrollLine> lines, CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, ct);
        var profiles = await db.EmployeeProfiles.AsNoTracking().ToDictionaryAsync(p => p.UserId, ct);
        var departments = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.NameAr, ct);

        return lines.Select(l =>
        {
            var user = users.GetValueOrDefault(l.EmployeeId);
            var profile = profiles.GetValueOrDefault(l.EmployeeId);
            return new PayslipDto(l.Id, l.EmployeeId, user?.FullName ?? "?", user?.EmployeeNumber,
                profile is null ? null : departments.GetValueOrDefault(profile.DepartmentId),
                l.BasicSalary, l.ScheduledDays, l.PresentDays, l.AbsentDays, l.LeaveDays, l.UnpaidLeaveDays,
                l.LateMinutes, l.OvertimeMinutes, l.Earnings, l.Deductions, l.CappedDeductions, l.NetPay,
                l.Items.OrderBy(i => i.IsDeduction).ThenBy(i => i.SourceKey == "salary" ? 0 : 1).ThenBy(i => i.Label, StringComparer.Ordinal).Select(i => new PayslipItemDto(i.Label, i.Amount, i.IsDeduction)).ToList());
        }).OrderBy(s => s.EmployeeName, StringComparer.Ordinal).ToList();
    }

    private async Task<PayrollCycle> Find(Guid id, CancellationToken ct) =>
        await db.PayrollCycles.SingleOrDefaultAsync(c => c.Id == id, ct)
        ?? throw new DomainException("payroll.not_found", "Payroll cycle not found.");

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Escape(string? value)
    {
        var text = value ?? string.Empty;
        return text.Contains(',', StringComparison.Ordinal) || text.Contains('"', StringComparison.Ordinal)
            ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : text;
    }
}
