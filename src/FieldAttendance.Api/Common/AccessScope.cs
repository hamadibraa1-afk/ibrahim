using System.Security.Claims;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Common;

/// <summary>
/// Supervisors are responsible for specific sites, so they see the sites they own and the
/// employees working there. Administrators and department managers see everything.
/// A supervisor with no sites assigned yet sees everything, so the system stays usable
/// before anyone has been mapped to a site.
/// Returning null means "no restriction".
/// </summary>
public sealed class AccessScope(AppDbContext db, ICurrentUser me, IHttpContextAccessor accessor, IClock clock)
{
    private const int HistoryDays = 60;
    private const int FutureDays = 120;

    private string? Role => accessor.HttpContext?.User.FindFirstValue(AppClaims.Role);

    private bool IsSupervisor => Role == nameof(UserRole.Supervisor);

    /// <summary>Administrators and HR see every employee's record; nobody else does by default.</summary>
    public bool SeesEveryone => Role is nameof(UserRole.SystemAdmin) or nameof(UserRole.HrManager) or nameof(UserRole.HrOfficer);

    public bool IsDepartmentManager => Role == nameof(UserRole.DepartmentManager);

    /// <summary>
    /// Guards a request for one person's data. An employee may read their own record; a
    /// department manager may read their department's; HR and administrators may read any.
    /// Anything else is refused, so an id in the URL cannot expose someone else's record.
    /// </summary>
    public async Task EnsureCanSeeEmployeeAsync(Guid employeeId, CancellationToken ct)
    {
        if (SeesEveryone) return;
        if (me.Id == employeeId) return;

        if (IsDepartmentManager && me.Id is { } managerId)
        {
            var mine = await db.EmployeeProfiles.AsNoTracking()
                .Where(p => p.UserId == employeeId)
                .Select(p => new { p.DepartmentId, p.ManagerId }).SingleOrDefaultAsync(ct);

            if (mine is not null)
            {
                if (mine.ManagerId == managerId) return;
                var managed = await db.Departments.AsNoTracking()
                    .AnyAsync(d => d.Id == mine.DepartmentId && d.ManagerId == managerId, ct);
                if (managed) return;
            }
        }

        if (IsSupervisor)
        {
            var allowed = await EmployeeIdsAsync(ct);
            if (allowed is null || allowed.Contains(employeeId)) return;
        }

        throw new DomainException("access.forbidden", "You are not allowed to view this employee's records.");
    }

    public async Task<HashSet<Guid>?> LocationIdsAsync(CancellationToken ct)
    {
        if (!IsSupervisor || me.Id is not { } id) return null;
        var mine = await db.SupervisorLocations.AsNoTracking()
            .Where(s => s.SupervisorId == id && s.IsActive).Select(s => s.LocationId).ToListAsync(ct);
        return mine.Count == 0 ? null : mine.ToHashSet();
    }

    /// <summary>Employees working at the supervisor's sites, plus anyone not yet assigned anywhere (so they can be staffed).</summary>
    public async Task<HashSet<Guid>?> EmployeeIdsAsync(CancellationToken ct)
    {
        var locations = await LocationIdsAsync(ct);
        if (locations is null) return null;

        var from = clock.Today.AddDays(-HistoryDays);
        var to = clock.Today.AddDays(FutureDays);

        var assigned = await db.Assignments.AsNoTracking()
            .Where(a => a.IsActive && locations.Contains(a.LocationId) && a.StartDate <= to && (a.EndDate == null || a.EndDate >= from))
            .Select(a => a.EmployeeId).ToListAsync(ct);
        var covering = await db.AssignmentOverrides.AsNoTracking()
            .Where(o => o.IsActive && o.LocationId != null && locations.Contains(o.LocationId!.Value) && o.FromDate <= to && o.ToDate >= from)
            .Select(o => o.EmployeeId).ToListAsync(ct);
        var everAssigned = await db.Assignments.AsNoTracking()
            .Where(a => a.IsActive && (a.EndDate == null || a.EndDate >= from)).Select(a => a.EmployeeId).ToListAsync(ct);
        var unassigned = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Role == UserRole.Collector && !everAssigned.Contains(u.Id))
            .Select(u => u.Id).ToListAsync(ct);

        return assigned.Concat(covering).Concat(unassigned).ToHashSet();
    }
}
