using System.ComponentModel.DataAnnotations;
using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Hr;

public sealed record DepartmentDto(Guid Id, string NameAr, string NameEn, Guid? BranchLocationId, string? BranchName,
    Guid? ManagerId, string? ManagerName, int SectionCount, int EmployeeCount, bool IsActive, byte[] RowVersion);

public sealed record SaveDepartmentRequest([Required] string NameAr, [Required] string NameEn,
    Guid? BranchLocationId, Guid? ManagerId, string? Notes, byte[]? RowVersion);

public sealed record SectionDto(Guid Id, Guid DepartmentId, string DepartmentName, string NameAr, string NameEn,
    Guid? HeadId, string? HeadName, int EmployeeCount, bool IsActive, byte[] RowVersion);

public sealed record SaveSectionRequest(Guid DepartmentId, [Required] string NameAr, [Required] string NameEn,
    Guid? HeadId, string? Notes, byte[]? RowVersion);

public sealed record HolidayDto(Guid Id, string NameAr, string NameEn, DateOnly FromDate, DateOnly ToDate, int Days, bool IsActive, byte[] RowVersion);

public sealed record SaveHolidayRequest([Required] string NameAr, [Required] string NameEn, DateOnly FromDate, DateOnly ToDate, byte[]? RowVersion);

/// <summary>Branches, departments, sections and the public holiday calendar.</summary>
[ApiController]
[Route("api/hr/org")]
[Authorize(Policy = HrPolicies.Read)]
public sealed class OrganizationController(AppDbContext db) : ControllerBase
{
    [HttpGet("departments")]
    public async Task<IReadOnlyList<DepartmentDto>> Departments([FromQuery] bool includeInactive, CancellationToken ct)
    {
        var departments = await db.Departments.AsNoTracking().Where(d => includeInactive || d.IsActive)
            .OrderBy(d => d.SortOrder).ThenBy(d => d.NameAr).ToListAsync(ct);
        var branches = await db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.NameAr, ct);
        var people = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var sectionCounts = await db.Sections.AsNoTracking().Where(s => s.IsActive)
            .GroupBy(s => s.DepartmentId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var employeeCounts = await db.EmployeeProfiles.AsNoTracking().Where(p => p.IsActive)
            .GroupBy(p => p.DepartmentId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);

        return departments.Select(d => new DepartmentDto(d.Id, d.NameAr, d.NameEn, d.BranchLocationId,
            d.BranchLocationId is { } b ? branches.GetValueOrDefault(b) : null,
            d.ManagerId, d.ManagerId is { } m ? people.GetValueOrDefault(m) : null,
            sectionCounts.FirstOrDefault(x => x.Key == d.Id)?.Count ?? 0,
            employeeCounts.FirstOrDefault(x => x.Key == d.Id)?.Count ?? 0,
            d.IsActive, d.RowVersion)).ToList();
    }

    [HttpPost("departments")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> CreateDepartment(SaveDepartmentRequest r, CancellationToken ct)
    {
        await EnsureBranchAsync(r.BranchLocationId, ct);
        db.Departments.Add(new Department(r.NameAr, r.NameEn, r.BranchLocationId, r.ManagerId, r.Notes));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("departments/{id:guid}")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> UpdateDepartment(Guid id, SaveDepartmentRequest r, CancellationToken ct)
    {
        var department = await db.Departments.SingleOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new DomainException("department.not_found", "Department not found.");
        await EnsureBranchAsync(r.BranchLocationId, ct);
        db.ExpectVersion(department, r.RowVersion);
        department.Rename(r.NameAr, r.NameEn, r.Notes);
        department.SetBranch(r.BranchLocationId);
        department.SetManager(r.ManagerId);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Logical delete, refused while employees or sections still belong to it.</summary>
    [HttpDelete("departments/{id:guid}")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> DeleteDepartment(Guid id, CancellationToken ct)
    {
        var department = await db.Departments.SingleOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new DomainException("department.not_found", "Department not found.");
        if (await db.EmployeeProfiles.AnyAsync(p => p.DepartmentId == id && p.IsActive, ct))
            throw new DomainException("department.in_use", "Move its employees to another department first.");
        if (await db.Sections.AnyAsync(s => s.DepartmentId == id && s.IsActive, ct))
            throw new DomainException("department.has_sections", "Remove or move its sections first.");
        department.Deactivate();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("sections")]
    public async Task<IReadOnlyList<SectionDto>> Sections([FromQuery] Guid? departmentId, [FromQuery] bool includeInactive, CancellationToken ct)
    {
        var query = db.Sections.AsNoTracking().Where(s => includeInactive || s.IsActive);
        if (departmentId is { } d) query = query.Where(s => s.DepartmentId == d);

        var sections = await query.OrderBy(s => s.NameAr).ToListAsync(ct);
        var departments = await db.Departments.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.NameAr, ct);
        var people = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var counts = await db.EmployeeProfiles.AsNoTracking().Where(p => p.IsActive && p.SectionId != null)
            .GroupBy(p => p.SectionId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);

        return sections.Select(s => new SectionDto(s.Id, s.DepartmentId, departments.GetValueOrDefault(s.DepartmentId, "?"),
            s.NameAr, s.NameEn, s.HeadId, s.HeadId is { } h ? people.GetValueOrDefault(h) : null,
            counts.FirstOrDefault(x => x.Key == s.Id)?.Count ?? 0, s.IsActive, s.RowVersion)).ToList();
    }

    [HttpPost("sections")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> CreateSection(SaveSectionRequest r, CancellationToken ct)
    {
        await EnsureDepartmentAsync(r.DepartmentId, ct);
        db.Sections.Add(new Section(r.DepartmentId, r.NameAr, r.NameEn, r.HeadId, r.Notes));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("sections/{id:guid}")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> UpdateSection(Guid id, SaveSectionRequest r, CancellationToken ct)
    {
        var section = await db.Sections.SingleOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new DomainException("section.not_found", "Section not found.");
        await EnsureDepartmentAsync(r.DepartmentId, ct);
        db.ExpectVersion(section, r.RowVersion);
        section.Rename(r.NameAr, r.NameEn, r.Notes);
        section.MoveToDepartment(r.DepartmentId);
        section.SetHead(r.HeadId);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("sections/{id:guid}")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> DeleteSection(Guid id, CancellationToken ct)
    {
        var section = await db.Sections.SingleOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new DomainException("section.not_found", "Section not found.");
        if (await db.EmployeeProfiles.AnyAsync(p => p.SectionId == id && p.IsActive, ct))
            throw new DomainException("section.in_use", "Move its employees first.");
        section.Deactivate();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("holidays")]
    public async Task<IReadOnlyList<HolidayDto>> Holidays([FromQuery] int? year, CancellationToken ct)
    {
        var list = await db.Holidays.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.FromDate).ToListAsync(ct);
        if (year is { } y) list = list.Where(h => h.FromDate.Year == y || h.ToDate.Year == y).ToList();
        return list.Select(h => new HolidayDto(h.Id, h.NameAr, h.NameEn, h.FromDate, h.ToDate, h.Days, h.IsActive, h.RowVersion)).ToList();
    }

    [HttpPost("holidays")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> CreateHoliday(SaveHolidayRequest r, CancellationToken ct)
    {
        db.Holidays.Add(new Holiday(r.NameAr, r.NameEn, r.FromDate, r.ToDate));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("holidays/{id:guid}")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> DeleteHoliday(Guid id, CancellationToken ct)
    {
        var holiday = await db.Holidays.SingleOrDefaultAsync(h => h.Id == id, ct)
            ?? throw new DomainException("holiday.not_found", "Holiday not found.");
        holiday.Deactivate();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task EnsureBranchAsync(Guid? branchLocationId, CancellationToken ct)
    {
        if (branchLocationId is not { } id) return;
        if (!await db.Locations.AnyAsync(l => l.Id == id && l.IsActive && l.Kind == LocationKind.Office, ct))
            throw new DomainException("branch.not_found", "Branch not found. Add it as an office location first.");
    }

    private async Task EnsureDepartmentAsync(Guid departmentId, CancellationToken ct)
    {
        if (!await db.Departments.AnyAsync(d => d.Id == departmentId && d.IsActive, ct))
            throw new DomainException("department.not_found", "Department not found.");
    }
}
