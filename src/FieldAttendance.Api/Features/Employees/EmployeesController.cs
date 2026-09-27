using System.ComponentModel.DataAnnotations;
using FieldAttendance.Api.Auth;
using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Employees;

public sealed record EmployeeDto(Guid Id, string FullName, string EmployeeNumber, string? Email, string Phone,
    string Role, string PreferredLanguage, bool IsActive, byte[] RowVersion);

public sealed record CreateEmployeeRequest(
    [Required] string FullName, [Required] string EmployeeNumber, [EmailAddress] string? Email, [Required] string Phone,
    [Required] string Role, [Required] string PreferredLanguage, [Required] string Password);

public sealed record UpdateEmployeeRequest(
    [Required] string FullName, [Required] string EmployeeNumber, [EmailAddress] string? Email, [Required] string Phone,
    [Required] string Role, [Required] string PreferredLanguage, byte[]? RowVersion);

public sealed record ResetPasswordRequest([Required] string Password);

public sealed record SetLocationsRequest(IReadOnlyList<Guid> LocationIds);

/// <summary>
/// User accounts for every role. Reading is open to supervisors, department managers and HR;
/// creating, editing and deleting accounts is restricted to the system administrator.
/// </summary>
[ApiController]
[Route("api/employees")]
[Authorize(Policy = Policies.ReadAny)]
public sealed class EmployeesController(AppDbContext db, ICurrentUser me, IClock clock, AccessScope scope) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<EmployeeDto>> List([FromQuery] bool includeInactive, [FromQuery] string? role, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking().Where(u => includeInactive || u.IsActive);
        if (Enum.TryParse<UserRole>(role, true, out var parsed)) query = query.Where(u => u.Role == parsed);

        var list = await query.OrderBy(u => u.FullName).ToListAsync(ct);
        var allowed = await scope.EmployeeIdsAsync(ct);
        if (allowed is not null)
            list = list.Where(u => u.Role != UserRole.Collector || allowed.Contains(u.Id)).ToList();
        return list.Select(ToDto).ToList();
    }

    /// <summary>
    /// Every account is an employee, the administrator included, so accounts are created in the HR
    /// module together with their HR record. This endpoint stays only to say where to go instead.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = Policies.Admin)]
    public ActionResult<EmployeeDto> Create(CreateEmployeeRequest r) =>
        throw new DomainException("user.create_in_hr", "Accounts are added from Human Resources, so each one has an HR record.");

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<EmployeeDto>> Update(Guid id, UpdateEmployeeRequest r, CancellationToken ct)
    {
        var user = await Find(id, ct);
        var role = ParseRole(r.Role);
        db.ExpectVersion(user, r.RowVersion);
        user.UpdateProfile(r.FullName, r.Email, r.Phone, r.EmployeeNumber);
        user.SetPreferredLanguage(r.PreferredLanguage);
        if (role != user.Role)
        {
            await EnsureNotLastAdmin(user, ct);
            if (user.Role == UserRole.Collector) await EndFieldWork(user.Id, ct);
            user.ChangeRole(role);
        }
        await db.SaveChangesAsync(ct);
        return ToDto(user);
    }

    /// <summary>Soft delete: the account can no longer sign in, but its history stays in reports.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<EmployeeDto>> Delete(Guid id, CancellationToken ct)
    {
        var user = await Find(id, ct);
        if (user.Id == me.RequiredId)
            throw new DomainException("user.self_delete", "You cannot delete your own account.");
        await EnsureNotLastAdmin(user, ct);
        user.Deactivate();
        if (user.Role == UserRole.Collector) await EndFieldWork(user.Id, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(user);
    }

    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<EmployeeDto>> Restore(Guid id, CancellationToken ct)
    {
        var user = await Find(id, ct);
        user.Activate();
        await db.SaveChangesAsync(ct);
        return ToDto(user);
    }

    [HttpPost("{id:guid}/reset-password")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest r, CancellationToken ct)
    {
        PasswordHasher.EnsureStrong(r.Password);
        var user = await Find(id, ct);
        user.SetPasswordHash(PasswordHasher.Hash(r.Password));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Ends current and future assignments and drops planned records nobody attended yet.</summary>
    private async Task EndFieldWork(Guid employeeId, CancellationToken ct)
    {
        var today = clock.Today;
        foreach (var a in await db.Assignments.Where(a => a.EmployeeId == employeeId && a.IsActive && (a.EndDate == null || a.EndDate >= today)).ToListAsync(ct))
            a.EndBefore(today);
        db.AttendanceRecords.RemoveRange(await db.AttendanceRecords
            .Where(x => x.EmployeeId == employeeId && x.ShiftDate >= today && x.CheckInAt == null).ToListAsync(ct));
    }

    private async Task EnsureNotLastAdmin(User user, CancellationToken ct)
    {
        if (user.Role != UserRole.SystemAdmin) return;
        if (await db.Users.CountAsync(u => u.IsActive && u.Role == UserRole.SystemAdmin, ct) <= 1)
            throw new DomainException("user.last_admin", "At least one active system administrator is required.");
    }

    /// <summary>Sites a supervisor is responsible for. Empty means every site.</summary>
    [HttpGet("{id:guid}/locations")]
    public async Task<IReadOnlyList<Guid>> Locations(Guid id, CancellationToken ct) =>
        await db.SupervisorLocations.AsNoTracking().Where(s => s.SupervisorId == id && s.IsActive)
            .Select(s => s.LocationId).ToListAsync(ct);

    [HttpPut("{id:guid}/locations")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> SetLocations(Guid id, SetLocationsRequest r, CancellationToken ct)
    {
        var user = await Find(id, ct);
        if (user.Role != UserRole.Supervisor)
            throw new DomainException("user.not_supervisor", "Only supervisors are mapped to sites.");

        var wanted = r.LocationIds.Distinct().ToList();
        if (wanted.Count > 0 && await db.Locations.CountAsync(l => wanted.Contains(l.Id) && l.IsActive, ct) != wanted.Count)
            throw new DomainException("location.not_found", "One of the selected sites is not available.");

        var current = await db.SupervisorLocations.Where(s => s.SupervisorId == id).ToListAsync(ct);
        db.SupervisorLocations.RemoveRange(current);
        foreach (var locationId in wanted)
            db.SupervisorLocations.Add(new SupervisorLocation(id, locationId));

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<User> Find(Guid id, CancellationToken ct) =>
        await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct)
        ?? throw new DomainException("employee.not_found", "Account not found.");

    private static UserRole ParseRole(string role) =>
        Enum.TryParse<UserRole>(role, true, out var r) ? r : throw new DomainException("user.invalid_role", "Unknown role.");

    private static EmployeeDto ToDto(User u) =>
        new(u.Id, u.FullName, u.EmployeeNumber, u.Email, u.Phone, u.Role.ToString(), u.PreferredLanguage, u.IsActive, u.RowVersion);
}
