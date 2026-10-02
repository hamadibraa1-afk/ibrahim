using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalSystem.Api.Common;
using ProposalSystem.Api.Data;
using ProposalSystem.Api.Domain;

namespace ProposalSystem.Api.Controllers;

public sealed record DepartmentDto(int Id, string Name, bool IsActive, int SortOrder, int UserCount);
public sealed record SaveDepartmentRequest(string Name, bool IsActive = true);

/// <summary>
/// Departments list, maintained in Settings by the administrator. User accounts choose their
/// department from it (drop-down), so the same department is never spelled two ways.
/// </summary>
[ApiController]
[Route("api/departments")]
[Authorize]
public sealed class DepartmentsController(AppDbContext db) : ControllerBase
{
    /// <summary>Every signed-in user may read the list (filters use it); only admins change it.</summary>
    [HttpGet]
    public async Task<List<DepartmentDto>> GetAll([FromQuery] bool activeOnly, CancellationToken ct)
    {
        var q = db.Departments.AsQueryable();
        if (activeOnly) q = q.Where(d => d.IsActive);
        var list = await q.OrderBy(d => d.SortOrder).ThenBy(d => d.Name).ToListAsync(ct);
        var counts = await db.Users.GroupBy(u => u.Department).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return list.Select(d => new DepartmentDto(d.Id, d.Name, d.IsActive, d.SortOrder, counts.GetValueOrDefault(d.Name))).ToList();
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<DepartmentDto> Create(SaveDepartmentRequest req, CancellationToken ct)
    {
        var name = Validate(req.Name);
        if (await db.Departments.AnyAsync(d => d.Name == name, ct))
            throw ApiException.Conflict("هذه الإدارة موجودة بالفعل.");
        var order = await db.Departments.Select(d => (int?)d.SortOrder).MaxAsync(ct) ?? -1;
        var dept = new Department { Name = name, IsActive = req.IsActive, SortOrder = order + 1 };
        db.Departments.Add(dept);
        await db.SaveChangesAsync(ct);
        return new DepartmentDto(dept.Id, dept.Name, dept.IsActive, dept.SortOrder, 0);
    }

    /// <summary>
    /// Renaming carries the new name to the users and proposals of that department, so reports
    /// and filters keep treating it as one department.
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<DepartmentDto> Update(int id, SaveDepartmentRequest req, CancellationToken ct)
    {
        var dept = await db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw ApiException.NotFound("الإدارة غير موجودة.");
        var name = Validate(req.Name);
        if (name != dept.Name)
        {
            if (await db.Departments.AnyAsync(d => d.Name == name && d.Id != id, ct))
                throw ApiException.Conflict("يوجد إدارة أخرى بهذا الاسم.");
            var old = dept.Name;
            await db.Users.Where(u => u.Department == old).ExecuteUpdateAsync(s => s.SetProperty(u => u.Department, name), ct);
            await db.Proposals.Where(p => p.Department == old).ExecuteUpdateAsync(s => s.SetProperty(p => p.Department, name), ct);
            dept.Name = name;
        }
        dept.IsActive = req.IsActive;
        await db.SaveChangesAsync(ct);
        var users = await db.Users.CountAsync(u => u.Department == dept.Name, ct);
        return new DepartmentDto(dept.Id, dept.Name, dept.IsActive, dept.SortOrder, users);
    }

    /// <summary>A department still assigned to users is deactivated instead, so their records stay valid.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var dept = await db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw ApiException.NotFound("الإدارة غير موجودة.");
        var users = await db.Users.CountAsync(u => u.Department == dept.Name, ct);
        if (users > 0)
            throw ApiException.Conflict($"لا يمكن حذف إدارة مرتبطة بـ {users} مستخدم. أوقفها بدلاً من ذلك أو انقل المستخدمين أولاً.");
        db.Departments.Remove(dept);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static string Validate(string? name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
            throw ApiException.BadRequest("اسم الإدارة إلزامي.");
        if (trimmed.Length > 150)
            throw ApiException.BadRequest("اسم الإدارة طويل جداً.");
        return trimmed;
    }
}
