using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Hr;

public sealed record MyProfileDto(string FullName, string EmployeeNumber, string? Email, string Phone, string Role,
    string? JobTitle, string? Department, string? Section, string? Branch, string? Manager, string? ScheduleName,
    DateOnly? HireDate, string? Status, string? Nationality, string? IdNumber, DateOnly? IdExpiry,
    string? PassportNumber, DateOnly? PassportExpiry, string? Iban, IReadOnlyList<MyScheduleDayDto> ScheduleDays);

public sealed record MyScheduleDayDto(DayOfWeek Day, TimeOnly StartTime, TimeOnly EndTime, int BreakMinutes);

public sealed record MyAttendanceDayDto(DateOnly Date, string LocationName, DateTimeOffset ScheduledStart,
    DateTimeOffset ScheduledEnd, DateTimeOffset? CheckInAt, DateTimeOffset? CheckOutAt, string Status,
    int NetWorkMinutes, int LateUnexcused, int EarlyUnexcused, int OvertimeMinutes);

public sealed record MyMonthDto(int Year, int Month, int ScheduledDays, int PresentDays, int AbsentDays, int LeaveDays,
    int NetWorkMinutes, int LateMinutes, int OvertimeMinutes, IReadOnlyList<MyAttendanceDayDto> Days);

public sealed record MyWarningDto(Guid Id, string LevelName, string Reason, DateTimeOffset IssuedAt,
    DateTimeOffset? AcknowledgedAt, string? Objection, string? ObjectionResponse);

public sealed record MyPayslipDto(int Year, int Month, decimal Earnings, decimal Deductions, decimal NetPay,
    string Status, IReadOnlyList<PayslipLine> Items);

public sealed record PayslipLine(string Label, decimal Amount, bool IsDeduction);

public sealed record ObjectionRequest(string Text);

/// <summary>
/// What an employee sees about themselves: their profile, their attendance, their warnings
/// and their payslips. Nothing here can reach another person's record.
///
/// One portal for every employee, field and office alike: every query is the caller's own, so the
/// gate is being signed in, not a role. It used to accept office roles only, which sent collectors
/// to a separate app and refused them their own profile and payslips.
/// </summary>
[ApiController]
[Route("api/my")]
[Authorize(Policy = Policies.SelfService)]
public sealed class MyPortalController(AppDbContext db, ICurrentUser me, IClock clock) : ControllerBase
{
    [HttpGet("profile")]
    public async Task<MyProfileDto> Profile(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == me.RequiredId, ct);
        var profile = await db.EmployeeProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == user.Id, ct);

        string? Name(Guid? id, IReadOnlyDictionary<Guid, string> source) => id is { } key ? source.GetValueOrDefault(key) : null;

        var departments = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.NameAr, ct);
        var sections = await db.Sections.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.NameAr, ct);
        var titles = await db.JobTitles.AsNoTracking().ToDictionaryAsync(j => j.Id, j => j.NameAr, ct);
        var branches = await db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.NameAr, ct);
        var people = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var schedule = profile?.WorkScheduleId is { } scheduleId
            ? await db.WorkSchedules.AsNoTracking().Include(s => s.Days).SingleOrDefaultAsync(s => s.Id == scheduleId, ct)
            : null;

        return new MyProfileDto(user.FullName, user.EmployeeNumber, user.Email, user.Phone, user.Role.ToString(),
            Name(profile?.JobTitleId, titles), Name(profile?.DepartmentId, departments), Name(profile?.SectionId, sections),
            Name(profile?.BranchLocationId, branches), Name(profile?.ManagerId, people), schedule?.NameAr,
            profile?.HireDate, profile?.Status.ToString(), profile?.Nationality, profile?.IdNumber, profile?.IdExpiry,
            profile?.PassportNumber, profile?.PassportExpiry, profile?.Iban,
            schedule is null ? [] : schedule.Days.OrderBy(d => d.Day)
                .Select(d => new MyScheduleDayDto(d.Day, d.StartTime, d.EndTime, d.BreakMinutes)).ToList());
    }

    [HttpGet("attendance")]
    public async Task<MyMonthDto> Attendance([FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        var today = clock.Today;
        var y = year ?? today.Year;
        var m = month ?? today.Month;
        if (m is < 1 or > 12) throw new DomainException("attendance.range_invalid", "Month is out of range.");

        var from = new DateOnly(y, m, 1);
        var to = new DateOnly(y, m, DateTime.DaysInMonth(y, m));

        var records = await db.AttendanceRecords.AsNoTracking()
            .Where(r => r.EmployeeId == me.RequiredId && r.ShiftDate >= from && r.ShiftDate <= to)
            .OrderBy(r => r.ShiftDate).ToListAsync(ct);
        var locations = await db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.NameAr, ct);

        var days = records.Select(r => new MyAttendanceDayDto(r.ShiftDate, locations.GetValueOrDefault(r.LocationId, "?"),
            r.ScheduledStart, r.ScheduledEnd, r.CheckInAt, r.CheckOutAt, r.Status.ToString(), r.NetWorkMinutes,
            r.LateUnexcused, r.EarlyUnexcused, r.OvertimeMinutes)).ToList();

        return new MyMonthDto(y, m, days.Count, days.Count(d => d.CheckInAt is not null),
            days.Count(d => d.Status == nameof(AttendanceStatus.Absent)),
            days.Count(d => d.Status == nameof(AttendanceStatus.OnLeave)),
            days.Sum(d => d.NetWorkMinutes), days.Sum(d => d.LateUnexcused), days.Sum(d => d.OvertimeMinutes), days);
    }

    [HttpGet("warnings")]
    public async Task<IReadOnlyList<MyWarningDto>> Warnings(CancellationToken ct)
    {
        var warnings = await db.Warnings.AsNoTracking().Where(w => w.EmployeeId == me.RequiredId && w.IsActive)
            .OrderByDescending(w => w.IssuedAt).ToListAsync(ct);
        var levels = await db.WarningLevels.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.NameAr, ct);
        return warnings.Select(w => new MyWarningDto(w.Id, levels.GetValueOrDefault(w.WarningLevelId, "?"), w.Reason,
            w.IssuedAt, w.AcknowledgedAt, w.Objection, w.ObjectionResponse)).ToList();
    }

    [HttpPost("warnings/{id:guid}/object")]
    public async Task<IActionResult> ObjectToWarning(Guid id, ObjectionRequest r, CancellationToken ct)
    {
        var warning = await db.Warnings.SingleOrDefaultAsync(w => w.Id == id && w.EmployeeId == me.RequiredId, ct)
            ?? throw new DomainException("warning.not_found", "Warning not found.");
        warning.FileObjection(r.Text, clock.Now);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Only closed months are shown: a draft payslip is not a promise.</summary>
    [HttpGet("payslips")]
    public async Task<IReadOnlyList<MyPayslipDto>> Payslips(CancellationToken ct)
    {
        var cycles = await db.PayrollCycles.AsNoTracking()
            .Where(c => c.Status == PayrollStatus.Approved || c.Status == PayrollStatus.Closed).ToListAsync(ct);
        var cycleIds = cycles.Select(c => c.Id).ToList();

        var lines = await db.PayrollLines.AsNoTracking().Include(l => l.Items)
            .Where(l => l.EmployeeId == me.RequiredId && cycleIds.Contains(l.PayrollCycleId)).ToListAsync(ct);

        return lines.Select(l =>
        {
            var cycle = cycles.Single(c => c.Id == l.PayrollCycleId);
            return new MyPayslipDto(cycle.Year, cycle.Month, l.Earnings, l.CappedDeductions, l.NetPay, cycle.Status.ToString(),
                l.Items.OrderBy(i => i.IsDeduction).ThenBy(i => i.SourceKey == "salary" ? 0 : 1).ThenBy(i => i.Label, StringComparer.Ordinal).Select(i => new PayslipLine(i.Label, i.Amount, i.IsDeduction)).ToList());
        }).OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).ToList();
    }
}
