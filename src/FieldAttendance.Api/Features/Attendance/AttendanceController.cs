using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Attendance;

public sealed record AttendanceRowDto(Guid Id, DateOnly ShiftDate, Guid EmployeeId, string EmployeeName, string? EmployeeNumber,
    string LocationName, string ShiftName, DateTimeOffset ScheduledStart, DateTimeOffset ScheduledEnd,
    DateTimeOffset? CheckInAt, string? CheckInType, double? CheckInDistance, DateTimeOffset? CheckOutAt, string? CheckOutType,
    string Status, int NetWorkMinutes, int PermissionMinutes, int LateExcused, int LateUnexcused,
    int EarlyExcused, int EarlyUnexcused, int OvertimeMinutes, IReadOnlyList<ExitDto> Exits, IReadOnlyList<string> Allowances);

public sealed record ExitDto(DateTimeOffset ExitAt, DateTimeOffset? ReturnAt, DateTimeOffset PermissionEnd);

[ApiController]
[Route("api/attendance")]
[Authorize(Policy = Policies.Read)]
public sealed class AttendanceController(AppDbContext db, AccessScope scope) : ControllerBase
{
    private const int MaxDays = 62;

    [HttpGet]
    public async Task<IReadOnlyList<AttendanceRowDto>> List([FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] Guid? employeeId, [FromQuery] Guid? locationId, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber >= MaxDays)
            throw new DomainException("attendance.range_invalid", $"Range must be 1–{MaxDays} days.");

        var query = db.AttendanceRecords.AsNoTracking().Include(r => r.Exits).Where(r => r.ShiftDate >= from && r.ShiftDate <= to);
        // An id in the query string must be authorised, or it becomes a way to read someone else's record.
        if (employeeId is { } e)
        {
            await scope.EnsureCanSeeEmployeeAsync(e, ct);
            query = query.Where(r => r.EmployeeId == e);
        }
        else if (!scope.SeesEveryone && !scope.IsDepartmentManager)
        {
            var allowed = await scope.EmployeeIdsAsync(ct);
            if (allowed is not null) query = query.Where(r => allowed.Contains(r.EmployeeId));
        }
        if (locationId is { } l) query = query.Where(r => r.LocationId == l);

        var scopedLocations = await scope.LocationIdsAsync(ct);
        if (scopedLocations is not null) query = query.Where(r => scopedLocations.Contains(r.LocationId));

        var records = await query.OrderByDescending(r => r.ShiftDate).ThenBy(r => r.ScheduledStart).Take(5000).ToListAsync(ct);
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, ct);
        var locations = await db.Locations.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.NameAr, ct);
        var shifts = await db.ShiftTemplates.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.NameAr, ct);

        // Allowances are scheduled by period, so every day they cover shows them for HR.
        var allowances = await db.EmployeeAllowances.AsNoTracking()
            .Where(a => a.IsActive && a.FromDate <= to && a.ToDate >= from).ToListAsync(ct);
        var allowanceTypes = await db.AllowanceTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.NameAr, ct);

        return records.Select(r => ToRow(r, users, locations, shifts,
            allowances.Where(a => a.EmployeeId == r.EmployeeId && a.Covers(r.ShiftDate))
                      .Select(a => allowanceTypes.GetValueOrDefault(a.AllowanceTypeId, "?")).ToList())).ToList();
    }

    internal static AttendanceRowDto ToRow(AttendanceRecord r, IReadOnlyDictionary<Guid, User> users,
        IReadOnlyDictionary<Guid, string> locations, IReadOnlyDictionary<Guid, string> shifts, IReadOnlyList<string> allowances)
    {
        var u = users.GetValueOrDefault(r.EmployeeId);
        return new AttendanceRowDto(r.Id, r.ShiftDate, r.EmployeeId, u?.FullName ?? "?", u?.EmployeeNumber,
            locations.GetValueOrDefault(r.LocationId, "?"), shifts.GetValueOrDefault(r.ShiftTemplateId, "?"),
            r.ScheduledStart, r.ScheduledEnd, r.CheckInAt, r.CheckInType?.ToString(), r.CheckInDistance,
            r.CheckOutAt, r.CheckOutType?.ToString(), r.Status.ToString(), r.NetWorkMinutes, r.PermissionMinutes,
            r.LateExcused, r.LateUnexcused, r.EarlyExcused, r.EarlyUnexcused, r.OvertimeMinutes,
            r.Exits.OrderBy(x => x.ExitAt).Select(x => new ExitDto(x.ExitAt, x.ReturnAt, x.PermissionEnd)).ToList(), allowances);
    }
}
