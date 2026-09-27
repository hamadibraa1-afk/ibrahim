using System.Security.Claims;
using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Api.Features.Approvals;
using FieldAttendance.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Requests;

/// <summary>
/// Who sees which requests, in one place. Leaves and permissions travel an approval chain, so the
/// inbox is what is waiting on the caller; geofence exceptions have no chain and go to whoever
/// runs the kind of place the day was worked at: supervisors for field sites, HR for offices.
/// </summary>
public sealed class RequestInbox(AppDbContext db, ApprovalService approvals, ICurrentUser me, AccessScope scope)
{
    private static bool Is(ClaimsPrincipal user, UserRole role) => user.IsInRole(role.ToString());

    /// <summary>Roles that review decided requests for their area; anyone else sees only what they decided.</summary>
    public static bool ReviewsHistory(ClaimsPrincipal user) =>
        Is(user, UserRole.SystemAdmin) || Is(user, UserRole.Supervisor) || Is(user, UserRole.DepartmentManager)
        || Is(user, UserRole.HrManager) || Is(user, UserRole.HrOfficer);

    /// <summary>
    /// Exceptions have no chain, so they go to whoever runs that kind of place. The administrator is
    /// not in it: like every other request, a decision belongs to the line, not to the system account.
    /// </summary>
    public static IReadOnlyList<LocationKind> ExceptionSites(ClaimsPrincipal user) =>
        Is(user, UserRole.Supervisor) || Is(user, UserRole.DepartmentManager) ? [LocationKind.Field]
        : Is(user, UserRole.HrManager) || Is(user, UserRole.HrOfficer) ? [LocationKind.Office]
        : [];

    public static bool CanDecideException(ClaimsPrincipal user, LocationKind site) =>
        (site == LocationKind.Field && Is(user, UserRole.Supervisor))
        || (site == LocationKind.Office && Is(user, UserRole.HrManager));

    /// <summary>Employees of one workforce, for narrowing a list; null means no narrowing.</summary>
    public IQueryable<Guid>? EmployeesOf(string? workforce) =>
        Enum.TryParse<Workforce>(workforce, true, out var side)
            ? db.EmployeeProfiles.Where(p => p.Workforce == side).Select(p => p.UserId)
            : null;

    /// <summary>The inbox is exactly what the caller can act on, for every role.</summary>
    public async Task<HashSet<Guid>> WaitingAsync(RequestKind kind, CancellationToken ct) =>
        (await approvals.WaitingOnAsync(kind, me.RequiredId, seeAll: false, ct)).ToHashSet();

    /// <summary>
    /// The badge counts only what the caller must act on: their own step in a chain, and the
    /// exceptions they may decide. HR's wider view of pending requests is not work for them.
    /// </summary>
    public async Task<PendingCountsDto> CountsAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var permissions = (await WaitingAsync(RequestKind.Permission, ct)).Count;
        var leaves = (await WaitingAsync(RequestKind.Leave, ct)).Count;

        var decidable = ExceptionSites(user).Where(k => CanDecideException(user, k)).ToList();
        var exceptions = 0;
        if (decidable.Count > 0)
        {
            var sites = db.Locations.Where(l => decidable.Contains(l.Kind)).Select(l => l.Id);
            var records = db.AttendanceRecords.Where(r => sites.Contains(r.LocationId)).Select(r => r.Id);
            var pending = await db.AttendanceExceptionRequests.AsNoTracking()
                .Where(e => e.IsActive && e.Status == RequestStatus.Pending && records.Contains(e.AttendanceRecordId))
                .Select(e => e.EmployeeId).ToListAsync(ct);
            var allowed = await scope.EmployeeIdsAsync(ct);
            exceptions = allowed is null ? pending.Count : pending.Count(allowed.Contains);
        }
        return new PendingCountsDto(permissions, exceptions, leaves);
    }
}
