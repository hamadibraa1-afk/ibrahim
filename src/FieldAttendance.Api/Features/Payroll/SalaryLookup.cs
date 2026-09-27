using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Payroll;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Payroll;

/// <summary>
/// Reads salary history so money is computed from the salary in effect on the day it concerns.
/// EmployeeProfile.BasicSalary is the latest figure entered, which may not have started yet.
/// </summary>
public sealed class SalaryLookup(AppDbContext db)
{
    public async Task<Dictionary<Guid, IReadOnlyCollection<SalaryStep>>> StepsAsync(IReadOnlyCollection<Guid> employeeIds, CancellationToken ct)
    {
        var rows = await db.SalaryChanges.AsNoTracking()
            .Where(s => s.IsActive && employeeIds.Contains(s.EmployeeId))
            .Select(s => new { s.EmployeeId, s.EffectiveFrom, s.OldSalary, s.NewSalary, s.CreatedAt })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.EmployeeId).ToDictionary(g => g.Key,
            g => (IReadOnlyCollection<SalaryStep>)g.Select(r => new SalaryStep(r.EffectiveFrom, r.OldSalary, r.NewSalary, r.CreatedAt)).ToList());
    }

    public async Task<decimal> OnAsync(EmployeeProfile profile, DateOnly day, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var steps = await StepsAsync([profile.UserId], ct);
        return SalaryTimeline.On(steps.GetValueOrDefault(profile.UserId, []), day, profile.BasicSalary);
    }
}
