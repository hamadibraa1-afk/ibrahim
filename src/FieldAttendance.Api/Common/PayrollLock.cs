using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Common;

/// <summary>
/// A month whose payroll has been approved or closed is financially settled: its attendance,
/// permissions, leaves and deductions can no longer be changed, because any change would make
/// the payslips that were already issued wrong.
///
/// Corrections for such a month are made deliberately in a later cycle, never by quietly
/// rewriting history.
/// </summary>
public sealed class PayrollLock(AppDbContext db)
{
    /// <summary>Throws when the date falls inside a settled month.</summary>
    public async Task EnsureOpenAsync(DateOnly date, CancellationToken ct)
    {
        if (await IsLockedAsync(date, ct))
            throw new DomainException("payroll.period_locked",
                "This month's payroll is already approved or closed; make the correction in a later cycle.");
    }

    /// <summary>Throws when any day in the range falls inside a settled month.</summary>
    public async Task EnsureRangeOpenAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        for (var month = new DateOnly(from.Year, from.Month, 1); month <= to; month = month.AddMonths(1))
            await EnsureOpenAsync(month, ct);
    }

    public Task<bool> IsLockedAsync(DateOnly date, CancellationToken ct) =>
        db.PayrollCycles.AsNoTracking().AnyAsync(c => c.Year == date.Year && c.Month == date.Month
            && (c.Status == PayrollStatus.Approved || c.Status == PayrollStatus.Closed), ct);
}
