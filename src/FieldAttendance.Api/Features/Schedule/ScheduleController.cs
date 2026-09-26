using System.ComponentModel.DataAnnotations;
using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Api.Features.Attendance;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Scheduling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Schedule;

public sealed record GridShiftDto(Guid LocationId, string LocationNameAr, string LocationNameEn, Guid ShiftTemplateId,
    string ShiftNameAr, string ShiftNameEn, DateTimeOffset Start, DateTimeOffset End, string Source, Guid SourceId,
    string? OverrideType, bool IsOnLeave);

public sealed record GridCellDto(DateOnly Date, IReadOnlyList<GridShiftDto> Shifts);

public sealed record GridRowDto(Guid EmployeeId, string EmployeeName, IReadOnlyList<GridCellDto> Cells);

/// <param name="Kind">Uncovered | Capacity</param>
public sealed record ScheduleWarningDto(string Kind, DateOnly Date, Guid LocationId, string LocationNameAr, string LocationNameEn, string? Detail);

public sealed record ScheduleGridDto(IReadOnlyList<DateOnly> Dates, IReadOnlyList<GridRowDto> Rows, IReadOnlyList<ScheduleWarningDto> Warnings);

public sealed record AssignmentDto(Guid Id, Guid EmployeeId, string EmployeeName, Guid LocationId, string LocationName,
    Guid ShiftTemplateId, string ShiftName, DateOnly StartDate, DateOnly? EndDate, int Days, string? Notes, byte[] RowVersion);

public sealed record CreateAssignmentRequest(Guid EmployeeId, Guid LocationId, Guid ShiftTemplateId, DateOnly StartDate,
    DateOnly? EndDate, [Range(1, 127)] int Days, string? Notes);

public sealed record SetEndDateRequest(DateOnly? EndDate, byte[]? RowVersion);

public sealed record EffectiveDateRequest(DateOnly EffectiveDate);

public sealed record PermanentTransferRequest(Guid LocationId, Guid ShiftTemplateId, DateOnly EffectiveDate);

/// <param name="Type">TemporaryTransfer | EmergencyCover | Cancel | Replacement</param>
/// <param name="EmployeeId">For Replacement: the employee being replaced.</param>
/// <param name="ReplacementEmployeeId">For Replacement: the employee taking over.</param>
public sealed record CreateOverrideRequest([Required] string Type, Guid EmployeeId, DateOnly FromDate, DateOnly ToDate,
    Guid? LocationId, Guid? ShiftTemplateId, Guid? ReplacementEmployeeId, string? Reason);

public sealed record AvailableEmployeeDto(Guid Id, string FullName);

/// <param name="StartTime">Working time for this employee at this location; each row may differ.</param>
public sealed record AssignEntry(Guid EmployeeId, TimeOnly StartTime, TimeOnly EndTime);

/// <param name="Days">Bitmask of working days, Sunday = 1 … Saturday = 64.</param>
public sealed record AssignToLocationRequest(Guid LocationId, DateOnly FromDate, DateOnly? ToDate, [Range(1, 127)] int Days,
    IReadOnlyList<AssignEntry> Entries);

[ApiController]
[Route("api/schedule")]
[Authorize(Policy = Policies.Read)]
public sealed class ScheduleController(AppDbContext db, ScheduleService schedule, ScheduleSnapshotLoader loader, IClock clock, AccessScope scope) : ControllerBase
{
    private const int MaxGridDays = 31;

    [HttpGet("grid")]
    public async Task<ScheduleGridDto> Grid([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber >= MaxGridDays)
            throw new DomainException("schedule.range_invalid", $"Range must be 1–{MaxGridDays} days.");

        var employees = await db.Users.AsNoTracking().Where(u => u.IsActive && u.Role == UserRole.Collector)
            .OrderBy(u => u.FullName).Select(u => new { u.Id, u.FullName }).ToListAsync(ct);
        var allowedEmployees = await scope.EmployeeIdsAsync(ct);
        if (allowedEmployees is not null) employees = employees.Where(e => allowedEmployees.Contains(e.Id)).ToList();
        var locations = await db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, ct);
        var scopedLocations = await scope.LocationIdsAsync(ct);
        var snapshot = await loader.LoadAsync(employees.Select(e => e.Id).ToList(), from, to, ct);

        var dates = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(from.AddDays).ToList();
        var all = new List<ResolvedShift>();
        var rows = employees.Select(e =>
        {
            var cells = dates.Select(d =>
            {
                var shifts = ScheduleResolver.Resolve(e.Id, d, snapshot);
                all.AddRange(shifts);
                return new GridCellDto(d, shifts.Select(s => ToDto(s, locations, snapshot)).ToList());
            }).ToList();
            return new GridRowDto(e.Id, e.FullName, cells);
        }).ToList();

        var warnings = new List<ScheduleWarningDto>();
        foreach (var d in dates)
        {
            var covered = all.Where(s => s.ShiftDate == d && !s.IsOnLeave).Select(s => s.LocationId).ToHashSet();
            warnings.AddRange(locations.Values.Where(l => l.IsActive && (scopedLocations?.Contains(l.Id) ?? true) && !covered.Contains(l.Id))
                .Select(l => new ScheduleWarningDto("Uncovered", d, l.Id, l.NameAr, l.NameEn, null)));
        }
        foreach (var l in locations.Values.Where(l => l.IsActive && (scopedLocations?.Contains(l.Id) ?? true)))
        {
            warnings.AddRange(ScheduleValidator.FindCapacityViolations(l.Id, l.Capacity, all)
                .Select(v => new ScheduleWarningDto("Capacity", DateOnly.FromDateTime(v.During.Start.DateTime), l.Id, l.NameAr, l.NameEn,
                    $"{v.Assigned}/{v.Capacity}")));
        }

        return new ScheduleGridDto(dates, rows, warnings.DistinctBy(w => (w.Kind, w.Date, w.LocationId)).ToList());
    }

    [HttpGet("assignments")]
    public async Task<IReadOnlyList<AssignmentDto>> Assignments([FromQuery] Guid? employeeId, [FromQuery] Guid? locationId, CancellationToken ct)
    {
        var today = clock.Today;
        var query = db.Assignments.AsNoTracking().Where(a => a.IsActive && (a.EndDate == null || a.EndDate >= today));
        if (employeeId is { } id) query = query.Where(a => a.EmployeeId == id);
        if (locationId is { } loc) query = query.Where(a => a.LocationId == loc);

        var list = await query.ToListAsync(ct);
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var locations = await db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.NameAr, ct);
        var shifts = await db.ShiftTemplates.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.NameAr, ct);

        return list.OrderBy(a => users.GetValueOrDefault(a.EmployeeId)).ThenBy(a => a.StartDate)
            .Select(a => new AssignmentDto(a.Id, a.EmployeeId, users.GetValueOrDefault(a.EmployeeId, "?"), a.LocationId,
                locations.GetValueOrDefault(a.LocationId, "?"), a.ShiftTemplateId, shifts.GetValueOrDefault(a.ShiftTemplateId, "?"),
                a.StartDate, a.EndDate, (int)a.Days, a.Notes, a.RowVersion))
            .ToList();
    }

    [HttpPost("assignments")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<IActionResult> CreateAssignment(CreateAssignmentRequest r, CancellationToken ct)
    {
        ScheduleService.EnsureNotInPast(r.StartDate, clock);
        await EnsureReferencesAsync(r.EmployeeId, r.LocationId, r.ShiftTemplateId, ct);
        var assignment = new Assignment(r.EmployeeId, r.LocationId, r.ShiftTemplateId, r.StartDate, r.EndDate, (WorkDays)r.Days, r.Notes);
        db.Assignments.Add(assignment);
        await schedule.SaveAsync([r.EmployeeId], r.StartDate, r.EndDate, ct);
        return Ok(new { assignment.Id });
    }

    /// <summary>
    /// Staffing a location directly: one or more employees, each with their own working times,
    /// over a date range. It only adds assignments for this location and never touches the
    /// employee's assignments elsewhere — but a time clash with another location is still rejected,
    /// because nobody can be in two places at once.
    /// </summary>
    [HttpPost("assign-to-location")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<IActionResult> AssignToLocation(AssignToLocationRequest r, CancellationToken ct)
    {
        if (r.Entries is not { Count: > 0 })
            throw new DomainException("schedule.no_employees", "Choose at least one employee.");
        ScheduleService.EnsureNotInPast(r.FromDate, clock);
        if (!await db.Locations.AnyAsync(l => l.Id == r.LocationId && l.IsActive, ct))
            throw new DomainException("location.not_found", "Location not found or inactive.");

        var employees = r.Entries.Select(e => e.EmployeeId).Distinct().ToList();
        var known = await db.Users.Where(u => employees.Contains(u.Id) && u.IsActive && u.Role == UserRole.Collector)
            .Select(u => u.Id).ToListAsync(ct);
        if (known.Count != employees.Count)
            throw new DomainException("employee.not_found", "One of the selected employees is not available.");

        foreach (var entry in r.Entries)
        {
            var shiftId = await schedule.EnsureShiftTemplateAsync(entry.StartTime, entry.EndTime, ct);
            db.Assignments.Add(new Assignment(entry.EmployeeId, r.LocationId, shiftId, r.FromDate, r.ToDate, (WorkDays)r.Days, null));
        }

        await schedule.SaveAsync(employees, r.FromDate, r.ToDate, ct);
        return NoContent();
    }

    /// <summary>"Extend" button, or shorten. Null = open-ended.</summary>
    [HttpPut("assignments/{id:guid}/end-date")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<IActionResult> SetEndDate(Guid id, SetEndDateRequest r, CancellationToken ct)
    {
        var a = await FindAssignment(id, ct);
        db.ExpectVersion(a, r.RowVersion);
        if (r.EndDate is { } end && end < clock.Today.AddDays(-1))
            throw new DomainException("schedule.past_date", "End date cannot be moved into the past.");
        a.SetEndDate(r.EndDate);
        await schedule.SaveAsync([a.EmployeeId], Max(a.StartDate, clock.Today), r.EndDate, ct);
        return NoContent();
    }

    /// <summary>Unassign from a date (the employee becomes available).</summary>
    [HttpPost("assignments/{id:guid}/end")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<IActionResult> End(Guid id, EffectiveDateRequest r, CancellationToken ct)
    {
        ScheduleService.EnsureNotInPast(r.EffectiveDate, clock);
        var a = await FindAssignment(id, ct);
        a.EndBefore(r.EffectiveDate);
        await schedule.SaveAsync([a.EmployeeId], r.EffectiveDate, r.EffectiveDate, ct);
        return NoContent();
    }

    [HttpPost("assignments/{id:guid}/transfer")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<IActionResult> PermanentTransfer(Guid id, PermanentTransferRequest r, CancellationToken ct)
    {
        ScheduleService.EnsureNotInPast(r.EffectiveDate, clock);
        var a = await FindAssignment(id, ct);
        await EnsureReferencesAsync(a.EmployeeId, r.LocationId, r.ShiftTemplateId, ct);
        var originalEnd = a.EndDate;
        a.EndBefore(r.EffectiveDate);
        db.Assignments.Add(new Assignment(a.EmployeeId, r.LocationId, r.ShiftTemplateId, r.EffectiveDate, originalEnd, a.Days, a.Notes));
        await schedule.SaveAsync([a.EmployeeId], r.EffectiveDate, originalEnd, ct);
        return NoContent();
    }

    [HttpPost("overrides")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<IActionResult> CreateOverride(CreateOverrideRequest r, CancellationToken ct)
    {
        ScheduleService.EnsureNotInPast(r.FromDate, clock);
        var type = ScheduleService.ParseOverrideType(r.Type);
        var affected = new List<Guid> { r.EmployeeId };

        if (type == OverrideType.Replacement)
        {
            var replacement = r.ReplacementEmployeeId
                ?? throw new DomainException("override.replaced_employee", "Choose the replacement employee.");
            await EnsureReferencesAsync(replacement, r.LocationId, r.ShiftTemplateId, ct);
            db.AssignmentOverrides.Add(new AssignmentOverride(r.EmployeeId, r.FromDate, r.ToDate, OverrideType.Cancel, null, null, null, r.Reason));
            db.AssignmentOverrides.Add(new AssignmentOverride(replacement, r.FromDate, r.ToDate, OverrideType.Replacement,
                r.LocationId, r.ShiftTemplateId, r.EmployeeId, r.Reason));
            affected.Add(replacement);
        }
        else
        {
            if (type != OverrideType.Cancel)
                await EnsureReferencesAsync(r.EmployeeId, r.LocationId, r.ShiftTemplateId, ct);
            db.AssignmentOverrides.Add(new AssignmentOverride(r.EmployeeId, r.FromDate, r.ToDate, type,
                r.LocationId, r.ShiftTemplateId, null, r.Reason));
        }

        await schedule.SaveAsync(affected, r.FromDate, r.ToDate, ct);
        return NoContent();
    }

    /// <summary>Undo an override: the base schedule applies again for its dates.</summary>
    [HttpPost("overrides/{id:guid}/deactivate")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<IActionResult> DeactivateOverride(Guid id, CancellationToken ct)
    {
        var o = await db.AssignmentOverrides.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new DomainException("override.not_found", "Override not found.");
        if (o.ToDate < clock.Today)
            throw new DomainException("schedule.past_date", "Past overrides cannot be changed.");
        o.Deactivate();
        await schedule.SaveAsync([o.EmployeeId], Max(o.FromDate, clock.Today), o.ToDate, ct);
        return NoContent();
    }

    [HttpGet("available")]
    public async Task<IReadOnlyList<AvailableEmployeeDto>> Available([FromQuery] DateOnly date, CancellationToken ct)
    {
        var ids = await schedule.AvailableEmployeesAsync(date, ct);
        var allowed = await scope.EmployeeIdsAsync(ct);
        if (allowed is not null) ids = ids.Where(allowed.Contains).ToList();
        return await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).OrderBy(u => u.FullName)
            .Select(u => new AvailableEmployeeDto(u.Id, u.FullName)).ToListAsync(ct);
    }

    private async Task<Assignment> FindAssignment(Guid id, CancellationToken ct) =>
        await db.Assignments.SingleOrDefaultAsync(a => a.Id == id && a.IsActive, ct)
        ?? throw new DomainException("assignment.not_found", "Assignment not found.");

    private async Task EnsureReferencesAsync(Guid employeeId, Guid? locationId, Guid? shiftId, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == employeeId && u.IsActive && u.Role == UserRole.Collector, ct))
            throw new DomainException("employee.not_found", "Employee not found or inactive.");
        if (locationId is { } l && !await db.Locations.AnyAsync(x => x.Id == l && x.IsActive, ct))
            throw new DomainException("location.not_found", "Location not found or inactive.");
        if (shiftId is { } s && !await db.ShiftTemplates.AnyAsync(x => x.Id == s && x.IsActive, ct))
            throw new DomainException("shift.not_found", "Shift not found or inactive.");
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static GridShiftDto ToDto(ResolvedShift s, IReadOnlyDictionary<Guid, Location> locations, ScheduleSnapshot snapshot)
    {
        var l = locations.GetValueOrDefault(s.LocationId);
        var t = snapshot.ShiftTemplates[s.ShiftTemplateId];
        return new GridShiftDto(s.LocationId, l?.NameAr ?? "?", l?.NameEn ?? "?", s.ShiftTemplateId, t.NameAr, t.NameEn,
            s.Window.Start, s.Window.End, s.Source.ToString(), s.SourceId, s.OverrideType?.ToString(), s.IsOnLeave);
    }
}
