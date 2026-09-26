using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Attendance;
using FieldAttendance.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Attendance;

public sealed record ComplianceDto(int Year, int Month, int CountedDays, int RequiredMinutes, int CompliantMinutes, decimal? Percent);

/// <summary>
/// The month-to-date compliance figure for one person. Reads the attendance records exactly as
/// the engine left them; the rule for what counts lives in <see cref="ComplianceCalculator"/>.
/// Callers authorise the employee id before asking.
/// </summary>
public sealed class ComplianceService(AppDbContext db, IClock clock)
{
    public async Task<ComplianceDto> ForMonthAsync(Guid employeeId, int? year, int? month, CancellationToken ct)
    {
        var today = clock.Today;
        var y = year ?? today.Year;
        var m = month ?? today.Month;
        if (m is < 1 or > 12 || y is < 2000 or > 2100)
            throw new DomainException("compliance.invalid_month", "Choose a valid month.");

        var from = new DateOnly(y, m, 1);
        var to = from.AddMonths(1).AddDays(-1);
        if (to > today) to = today;

        var records = from > today ? []
            : await db.AttendanceRecords.AsNoTracking()
                .Where(r => r.EmployeeId == employeeId && r.IsActive && r.ShiftDate >= from && r.ShiftDate <= to)
                .ToListAsync(ct);

        var result = ComplianceCalculator.Calculate(records.Select(ComplianceDay.From));
        return new ComplianceDto(y, m, result.CountedDays, result.RequiredMinutes, result.CompliantMinutes, result.Percent);
    }
}
