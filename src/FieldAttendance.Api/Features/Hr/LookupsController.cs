using System.ComponentModel.DataAnnotations;
using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Hr;

public sealed record LookupDto(Guid Id, string NameAr, string NameEn, string? Notes, int SortOrder, bool IsActive, byte[] RowVersion);

public sealed record SaveLookupRequest([Required] string NameAr, [Required] string NameEn, string? Notes, int SortOrder, byte[]? RowVersion);

/// <summary>
/// The configurable lists behind HR settings. One controller serves them all so adding a new
/// list later is a single line here instead of another copy of the same CRUD code.
/// Deletion is always logical: a value used by past records is deactivated, never removed,
/// so historical reports keep their meaning.
/// </summary>
[ApiController]
[Route("api/hr/lookups/{set}")]
[Authorize(Policy = HrPolicies.Read)]
public sealed class LookupsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<LookupDto>> List(string set, [FromQuery] bool includeInactive, CancellationToken ct) =>
        (await Query(set).AsNoTracking().Where(x => includeInactive || x.IsActive)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.NameAr).ToListAsync(ct)).Select(ToDto).ToList();

    [HttpPost]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<ActionResult<LookupDto>> Create(string set, SaveLookupRequest r, CancellationToken ct)
    {
        LookupEntity entity = Kind(set) switch
        {
            "job-titles" => new JobTitle(r.NameAr, r.NameEn, r.Notes),
            "grades" => new Grade(r.NameAr, r.NameEn, r.Notes),
            _ => new ContractType(r.NameAr, r.NameEn, r.Notes),
        };
        entity.SetSortOrder(r.SortOrder);
        db.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<ActionResult<LookupDto>> Update(string set, Guid id, SaveLookupRequest r, CancellationToken ct)
    {
        var entity = await Find(set, id, ct);
        db.ExpectVersion(entity, r.RowVersion);
        entity.Rename(r.NameAr, r.NameEn, r.Notes);
        entity.SetSortOrder(r.SortOrder);
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> Delete(string set, Guid id, CancellationToken ct)
    {
        var entity = await Find(set, id, ct);
        entity.Deactivate();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> Restore(string set, Guid id, CancellationToken ct)
    {
        var entity = await Find(set, id, ct);
        entity.Activate();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private IQueryable<LookupEntity> Query(string set) => Kind(set) switch
    {
        "job-titles" => db.JobTitles,
        "grades" => db.Grades,
        _ => db.ContractTypes,
    };

    private async Task<LookupEntity> Find(string set, Guid id, CancellationToken ct) =>
        await Query(set).SingleOrDefaultAsync(x => x.Id == id, ct)
        ?? throw new DomainException("lookup.not_found", "Item not found.");

    private static string Kind(string set) => set switch
    {
        "job-titles" or "grades" or "contract-types" => set,
        _ => throw new DomainException("lookup.unknown_set", "Unknown settings list."),
    };

    private static LookupDto ToDto(LookupEntity x) =>
        new(x.Id, x.NameAr, x.NameEn, x.Notes, x.SortOrder, x.IsActive, x.RowVersion);
}
