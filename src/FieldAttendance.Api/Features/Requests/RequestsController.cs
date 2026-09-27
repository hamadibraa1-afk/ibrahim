using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Api.Features.Attendance;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Geo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Requests;

public sealed record RejectRequest(string Reason);

public sealed record OnBehalfPermissionRequest(Guid EmployeeId, string Type, DateOnly ShiftDate, TimeOnly? FromTime, TimeOnly? ToTime, string Reason);

public sealed record ExceptionDto(Guid Id, Guid EmployeeId, string EmployeeName, string Kind, DateTimeOffset RequestedAt,
    string LocationName, double Latitude, double Longitude, double LocationLatitude, double LocationLongitude, int LocationRadius,
    double DistanceMeters, string? Reason, string Status, string? RejectReason, string? DecidedByName);

public sealed record PendingCountsDto(int Permissions, int Exceptions, int Leaves);

[ApiController]
[Route("api/requests")]
[Authorize(Policy = Policies.Approver)]
public sealed class RequestsController(AppDbContext db, PermissionService permissions, RecalculationService recalculator,
    ICurrentUser me, IClock clock, AccessScope scope, PayrollLock payrollLock, RequestInbox inbox) : ControllerBase
{
    /// <summary>What the caller has to act on, and nothing else: office requests no longer count on a field supervisor's badge.</summary>
    [HttpGet("pending-counts")]
    public Task<PendingCountsDto> PendingCounts(CancellationToken ct) => inbox.CountsAsync(User, ct);

    /// <summary>
    /// Pending: what is waiting on the caller in the approval chain. Decided: the caller's area for
    /// reviewers, otherwise only what the caller decided. <paramref name="workforce"/> narrows to
    /// field or office staff (the field module always asks for Field).
    /// </summary>
    [HttpGet("permissions")]
    public async Task<IReadOnlyList<PermissionDto>> Permissions([FromQuery] string status = "Pending", [FromQuery] string? workforce = null,
        CancellationToken ct = default)
    {
        var parsed = ParseStatus(status);
        var query = db.PermissionRequests.AsNoTracking().Where(p => p.IsActive && p.Status == parsed);
        if (inbox.EmployeesOf(workforce) is { } side) query = query.Where(p => side.Contains(p.EmployeeId));
        var list = await query.OrderBy(p => p.ShiftDate).ThenBy(p => p.CreatedAt).Take(500).ToListAsync(ct);

        if (parsed == RequestStatus.Pending)
        {
            var waiting = await inbox.WaitingAsync(RequestKind.Permission, ct);
            list = list.Where(p => waiting.Contains(p.Id)).ToList();
        }
        else if (!RequestInbox.ReviewsHistory(User))
        {
            list = list.Where(p => p.DecidedBy == me.RequiredId).ToList();
        }
        else
        {
            var allowed = await scope.EmployeeIdsAsync(ct);
            if (allowed is not null) list = list.Where(p => allowed.Contains(p.EmployeeId)).ToList();
        }
        var names = await NamesAsync(list.Select(p => p.EmployeeId).Concat(list.Where(p => p.DecidedBy != null).Select(p => p.DecidedBy!.Value)), ct);
        return list.Select(p => MyAttendanceController.ToDto(p) with
        {
            EmployeeName = names.GetValueOrDefault(p.EmployeeId),
            DecidedByName = p.DecidedBy is { } d ? names.GetValueOrDefault(d) : null,
        }).ToList();
    }

    [HttpPost("permissions")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<ActionResult<PermissionDto>> SubmitOnBehalf(OnBehalfPermissionRequest r, CancellationToken ct)
    {
        var p = await permissions.SubmitAsync(r.EmployeeId, me.RequiredId,
            new SubmitPermissionRequest(r.Type, r.ShiftDate, r.FromTime, r.ToTime, r.Reason), ct);
        return MyAttendanceController.ToDto(p);
    }

    /// <summary>Only the person the permission is waiting on may sign; ApprovalService enforces it.</summary>
    [HttpPost("permissions/{id:guid}/approve")]
    public async Task<IActionResult> ApprovePermission(Guid id, CancellationToken ct)
    {
        await EnsurePeriodOpenAsync(id, ct);
        await permissions.DecideAsync(id, me.RequiredId, approve: true, null, ct);
        return NoContent();
    }

    [HttpPost("permissions/{id:guid}/reject")]
    public async Task<IActionResult> RejectPermission(Guid id, RejectRequest r, CancellationToken ct)
    {
        await permissions.DecideAsync(id, me.RequiredId, approve: false, r.Reason, ct);
        return NoContent();
    }

    /// <summary>Exceptions at field sites go to supervisors, at office branches to HR; see <see cref="RequestInbox"/>.</summary>
    [HttpGet("exceptions")]
    public async Task<IReadOnlyList<ExceptionDto>> Exceptions([FromQuery] string status = "Pending", [FromQuery] string? workforce = null,
        CancellationToken ct = default)
    {
        var parsed = ParseStatus(status);
        var kinds = RequestInbox.ExceptionSites(User).ToList();
        if (Enum.TryParse<Workforce>(workforce, true, out var side))
            kinds = kinds.Where(k => k == (side == Workforce.Field ? LocationKind.Field : LocationKind.Office)).ToList();
        if (kinds.Count == 0) return [];

        var sites = db.Locations.Where(l => kinds.Contains(l.Kind)).Select(l => l.Id);
        var records = db.AttendanceRecords.Where(r => sites.Contains(r.LocationId)).Select(r => r.Id);
        var list = await db.AttendanceExceptionRequests.AsNoTracking()
            .Where(e => e.IsActive && e.Status == parsed && records.Contains(e.AttendanceRecordId))
            .OrderBy(e => e.RequestedAt).Take(500).ToListAsync(ct);
        var allowed = await scope.EmployeeIdsAsync(ct);
        if (allowed is not null) list = list.Where(e => allowed.Contains(e.EmployeeId)).ToList();
        var recordIds = list.Select(e => e.AttendanceRecordId).ToList();
        var recordLocations = await db.AttendanceRecords.AsNoTracking().Where(r => recordIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.LocationId, ct);
        var locations = await db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, ct);
        var names = await NamesAsync(list.Select(e => e.EmployeeId).Concat(list.Where(e => e.DecidedBy != null).Select(e => e.DecidedBy!.Value)), ct);

        return list.Select(e =>
        {
            var l = locations[recordLocations[e.AttendanceRecordId]];
            return new ExceptionDto(e.Id, e.EmployeeId, names.GetValueOrDefault(e.EmployeeId, "?"), e.Kind.ToString(), e.RequestedAt,
                l.NameAr, e.Latitude, e.Longitude, l.Latitude, l.Longitude, l.RadiusMeters, e.DistanceMeters, e.Reason,
                e.Status.ToString(), e.RejectReason, e.DecidedBy is { } d ? names.GetValueOrDefault(d) : null);
        }).ToList();
    }

    /// <summary>Records the check-in/out at the moment the employee asked, not the approval time (spec 3.3).</summary>
    [HttpPost("exceptions/{id:guid}/approve")]
    public async Task<IActionResult> ApproveException(Guid id, CancellationToken ct)
    {
        var e = await db.AttendanceExceptionRequests.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new DomainException("request.not_found", "Request not found.");
        var record = await db.AttendanceRecords.Include(r => r.Exits).SingleAsync(r => r.Id == e.AttendanceRecordId, ct);
        await EnsureCanDecideExceptionAsync(e.EmployeeId, record.LocationId, ct);
        await payrollLock.EnsureOpenAsync(record.ShiftDate, ct);

        e.Approve(me.RequiredId, clock.Now);
        var point = new GeoPoint(e.Latitude, e.Longitude);
        if (e.Kind == ExceptionKind.CheckIn)
            record.CheckIn(e.RequestedAt, point, 0, e.DistanceMeters, CheckInType.Exception);
        else
            record.CheckOut(e.RequestedAt, point, CheckOutType.Exception);

        await recalculator.RecalculateAsync(record, ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("exceptions/{id:guid}/reject")]
    public async Task<IActionResult> RejectException(Guid id, RejectRequest r, CancellationToken ct)
    {
        var e = await db.AttendanceExceptionRequests.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new DomainException("request.not_found", "Request not found.");
        var locationId = await db.AttendanceRecords.Where(x => x.Id == e.AttendanceRecordId).Select(x => x.LocationId).SingleAsync(ct);
        await EnsureCanDecideExceptionAsync(e.EmployeeId, locationId, ct);
        e.Reject(me.RequiredId, clock.Now, r.Reason);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
    }

    private static RequestStatus ParseStatus(string status) =>
        Enum.TryParse<RequestStatus>(status, true, out var s) ? s : RequestStatus.Pending;

    /// <summary>
    /// A field exception is the supervisor's to decide, and only for their own sites' staff; an office
    /// one is HR's. Before, any supervisor could decide any exception whose id they had.
    /// </summary>
    private async Task EnsureCanDecideExceptionAsync(Guid employeeId, Guid locationId, CancellationToken ct)
    {
        var kind = await db.Locations.Where(l => l.Id == locationId).Select(l => l.Kind).SingleAsync(ct);
        if (!RequestInbox.CanDecideException(User, kind))
            throw new DomainException("access.forbidden", "This request is decided by someone else.");
        if (kind == LocationKind.Field && await scope.EmployeeIdsAsync(ct) is { } allowed && !allowed.Contains(employeeId))
            throw new DomainException("access.forbidden", "This request is decided by someone else.");
    }

    /// <summary>A permission for a month whose payroll is settled can no longer change the numbers.</summary>
    private async Task EnsurePeriodOpenAsync(Guid permissionId, CancellationToken ct)
    {
        var shiftDate = await db.PermissionRequests.AsNoTracking()
            .Where(p => p.Id == permissionId).Select(p => (DateOnly?)p.ShiftDate).SingleOrDefaultAsync(ct);
        if (shiftDate is { } date) await payrollLock.EnsureOpenAsync(date, ct);
    }
}
