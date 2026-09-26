using System.ComponentModel.DataAnnotations;
using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Allowances;

public sealed record AllowanceTypeDto(Guid Id, string NameAr, string NameEn, decimal? DailyAmount, string? Notes, bool IsActive, byte[] RowVersion);

public sealed record SaveAllowanceTypeRequest([Required] string NameAr, [Required] string NameEn, decimal? DailyAmount, string? Notes, byte[]? RowVersion);

public sealed record AllowanceDto(Guid Id, Guid EmployeeId, string EmployeeName, string? EmployeeNumber, Guid AllowanceTypeId,
    string AllowanceTypeName, decimal? DailyAmount, DateOnly FromDate, DateOnly ToDate, int Days, string? Notes, bool IsActive);

/// <param name="Weeks">Convenience: when set, the period is this many weeks from FromDate.</param>
public sealed record GrantAllowanceRequest(IReadOnlyList<Guid> EmployeeIds, Guid AllowanceTypeId, DateOnly FromDate,
    DateOnly? ToDate, int? Days, int? Weeks, string? Notes);

[ApiController]
[Route("api/allowance-types")]
[Authorize(Policy = Policies.Read)]
public sealed class AllowanceTypesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<AllowanceTypeDto>> List([FromQuery] bool includeInactive, CancellationToken ct) =>
        (await db.AllowanceTypes.AsNoTracking().Where(t => includeInactive || t.IsActive).OrderBy(t => t.NameAr).ToListAsync(ct))
            .Select(ToDto).ToList();

    [HttpPost]
    [Authorize(Policy = Policies.Manage)]
    public async Task<ActionResult<AllowanceTypeDto>> Create(SaveAllowanceTypeRequest r, CancellationToken ct)
    {
        var type = new AllowanceType(r.NameAr, r.NameEn, r.DailyAmount, r.Notes);
        db.AllowanceTypes.Add(type);
        await db.SaveChangesAsync(ct);
        return ToDto(type);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<ActionResult<AllowanceTypeDto>> Update(Guid id, SaveAllowanceTypeRequest r, CancellationToken ct)
    {
        var type = await Find(id, ct);
        db.ExpectVersion(type, r.RowVersion);
        type.Update(r.NameAr, r.NameEn, r.DailyAmount, r.Notes);
        await db.SaveChangesAsync(ct);
        return ToDto(type);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var type = await Find(id, ct);
        type.Deactivate();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<AllowanceType> Find(Guid id, CancellationToken ct) =>
        await db.AllowanceTypes.SingleOrDefaultAsync(t => t.Id == id, ct)
        ?? throw new DomainException("allowance_type.not_found", "Allowance type not found.");

    private static AllowanceTypeDto ToDto(AllowanceType t) =>
        new(t.Id, t.NameAr, t.NameEn, t.DailyAmount, t.Notes, t.IsActive, t.RowVersion);
}

/// <summary>
/// Allowances granted to employees for a period. They are scheduled by date, so the attendance
/// report shows them on every day they cover — which is what HR needs for payroll.
/// </summary>
[ApiController]
[Route("api/allowances")]
[Authorize(Policy = Policies.Read)]
public sealed class AllowancesController(AppDbContext db, AccessScope scope) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<AllowanceDto>> List([FromQuery] Guid? employeeId, [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to, [FromQuery] bool includeInactive, CancellationToken ct)
    {
        var query = db.EmployeeAllowances.AsNoTracking().Where(a => includeInactive || a.IsActive);
        if (employeeId is { } e) query = query.Where(a => a.EmployeeId == e);
        if (from is { } f) query = query.Where(a => a.ToDate >= f);
        if (to is { } t) query = query.Where(a => a.FromDate <= t);

        var allowed = await scope.EmployeeIdsAsync(ct);
        if (allowed is not null) query = query.Where(a => allowed.Contains(a.EmployeeId));

        var list = await query.OrderByDescending(a => a.FromDate).Take(1000).ToListAsync(ct);
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, ct);
        var types = await db.AllowanceTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, ct);

        return list.Select(a =>
        {
            var u = users.GetValueOrDefault(a.EmployeeId);
            var t = types.GetValueOrDefault(a.AllowanceTypeId);
            return new AllowanceDto(a.Id, a.EmployeeId, u?.FullName ?? "?", u?.EmployeeNumber, a.AllowanceTypeId,
                t?.NameAr ?? "?", t?.DailyAmount, a.FromDate, a.ToDate, a.Days, a.Notes, a.IsActive);
        }).ToList();
    }

    /// <summary>Grants one allowance to one or more employees. The period is a date range, a number of days, or weeks.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.Manage)]
    public async Task<IActionResult> Grant(GrantAllowanceRequest r, CancellationToken ct)
    {
        if (r.EmployeeIds is not { Count: > 0 })
            throw new DomainException("allowance.no_employees", "Choose at least one employee.");
        if (!await db.AllowanceTypes.AnyAsync(t => t.Id == r.AllowanceTypeId && t.IsActive, ct))
            throw new DomainException("allowance_type.not_found", "Allowance type not found.");

        var to = r.ToDate
            ?? (r.Weeks is { } w and > 0 ? r.FromDate.AddDays(w * 7 - 1)
                : r.Days is { } d and > 0 ? r.FromDate.AddDays(d - 1)
                : throw new DomainException("allowance.period_required", "Set an end date, a number of days, or weeks."));

        var existing = await db.EmployeeAllowances
            .Where(a => a.IsActive && a.AllowanceTypeId == r.AllowanceTypeId && r.EmployeeIds.Contains(a.EmployeeId)
                        && a.FromDate <= to && a.ToDate >= r.FromDate)
            .ToListAsync(ct);

        foreach (var employeeId in r.EmployeeIds.Distinct())
        {
            var candidate = new EmployeeAllowance(employeeId, r.AllowanceTypeId, r.FromDate, to, r.Notes);
            if (existing.Any(candidate.Overlaps))
                throw new DomainException("allowance.overlap", "This employee already has the same allowance in that period.");
            db.EmployeeAllowances.Add(candidate);
        }

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var allowance = await db.EmployeeAllowances.SingleOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new DomainException("allowance.not_found", "Allowance not found.");
        allowance.Deactivate();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
