using System.ComponentModel.DataAnnotations;
using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Api.Features.Discipline;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Payroll;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Payroll;

public sealed record ExtraPaymentDto(Guid Id, Guid EmployeeId, string EmployeeName, string? EmployeeNumber, int Year, int Month,
    ExtraPaymentKind Kind, decimal Amount, int? OvertimeMinutes, string Reason);

/// <param name="Hours">Overtime only: hours to pay at the overtime rate. Give this or <paramref name="Amount"/>.</param>
public sealed record AddExtraPaymentRequest(Guid EmployeeId, [Range(2020, 2100)] int Year, [Range(1, 12)] int Month,
    ExtraPaymentKind Kind, [Range(0.01, 1000000)] decimal? Amount, [Range(0.01, 744)] decimal? Hours, [Required] string Reason);

/// <summary>
/// Bonuses and overtime HR decides to pay for a month. Nothing here is automatic: recorded
/// overtime is only information until HR pays it. Amounts are fixed on entry.
/// </summary>
[ApiController]
[Route("api/hr/payroll/extra-payments")]
[Authorize(Policy = HrPolicies.Manage)]
public sealed class ExtraPaymentsController(AppDbContext db, DeductionService deductions, SalaryLookup salaries,
    PayrollLock payrollLock) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<ExtraPaymentDto>> List([FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        var payments = await db.ExtraPayments.AsNoTracking()
            .Where(p => p.IsActive && p.Year == year && p.Month == month).OrderBy(p => p.CreatedAt).ToListAsync(ct);
        var ids = payments.Select(p => p.EmployeeId).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        return payments.Select(p => ToDto(p, users.GetValueOrDefault(p.EmployeeId))).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<ExtraPaymentDto>> Add(AddExtraPaymentRequest r, CancellationToken ct)
    {
        var first = new DateOnly(r.Year, r.Month, 1);
        await payrollLock.EnsureOpenAsync(first, ct);
        var profile = await db.EmployeeProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == r.EmployeeId, ct)
            ?? throw new DomainException("profile.not_found", "Employee not found.");

        int? minutes = null;
        decimal amount;
        if (r.Kind == ExtraPaymentKind.Overtime && r.Hours is { } hours)
        {
            // Priced now, from the month's basic and the month's rates, and then kept as entered.
            minutes = (int)Math.Round(hours * 60m, MidpointRounding.AwayFromZero);
            var steps = await salaries.StepsAsync([profile.UserId], ct);
            var basic = SalaryTimeline.ForPeriod(steps.GetValueOrDefault(profile.UserId, []), first,
                first.AddMonths(1).AddDays(-1), profile.BasicSalary);
            amount = PayrollMath.OvertimeAmount(basic, await PolicyForAsync(r.Year, r.Month, ct), minutes.Value);
        }
        else
        {
            amount = r.Amount ?? throw new DomainException("extra_payment.amount", "Enter an amount.");
        }

        var payment = new ExtraPayment(profile.UserId, r.Year, r.Month, r.Kind, amount, minutes, r.Reason);
        db.ExtraPayments.Add(payment);
        await db.SaveChangesAsync(ct);
        return ToDto(payment, await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == payment.EmployeeId, ct));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        var payment = await db.ExtraPayments.SingleOrDefaultAsync(p => p.Id == id && p.IsActive, ct)
            ?? throw new DomainException("extra_payment.not_found", "Payment not found.");
        await payrollLock.EnsureOpenAsync(payment.FirstDay, ct);
        payment.Deactivate();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>The month's own rates once its cycle is open, so the payment matches the payslip.</summary>
    private async Task<PayrollPolicy> PolicyForAsync(int year, int month, CancellationToken ct)
    {
        var cycle = await db.PayrollCycles.AsNoTracking().SingleOrDefaultAsync(c => c.Year == year && c.Month == month, ct);
        return cycle is null
            ? await deductions.PolicyAsync(ct)
            : new PayrollPolicy(cycle.MonthDays, cycle.DailyWorkMinutes, cycle.OvertimeFactor, cycle.MaxDeductionPercent);
    }

    private static ExtraPaymentDto ToDto(ExtraPayment p, User? user) =>
        new(p.Id, p.EmployeeId, user?.FullName ?? "?", user?.EmployeeNumber, p.Year, p.Month, p.Kind, p.Amount, p.OvertimeMinutes, p.Reason);
}
