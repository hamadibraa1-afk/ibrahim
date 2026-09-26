using System.ComponentModel.DataAnnotations;
using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Api.Features.Requests;
using FieldAttendance.Domain.Attendance;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Geo;
using FieldAttendance.Domain.Scheduling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FieldAttendance.Api.Features.Attendance;

public sealed record GpsFix([Range(-90, 90)] double Latitude, [Range(-180, 180)] double Longitude, [Range(0, 100000)] double Accuracy);

public sealed record ExitRequest(Guid PermissionId);

public sealed record ExceptionSubmit([Required] string Kind, [Range(-90, 90)] double Latitude, [Range(-180, 180)] double Longitude,
    double Accuracy, [Required] string Reason);

public sealed record PermissionDto(Guid Id, string Type, DateOnly ShiftDate, TimeOnly? FromTime, TimeOnly? ToTime, string Status,
    string? Reason, string? RejectReason, DateTimeOffset CreatedAt, string? EmployeeName = null, string? DecidedByName = null);

public sealed record TodayShiftDto(
    Guid RecordId, DateOnly ShiftDate, string LocationNameAr, string LocationNameEn, double LocationLatitude, double LocationLongitude,
    int LocationRadius, string ShiftNameAr, string ShiftNameEn, DateTimeOffset ScheduledStart, DateTimeOffset ScheduledEnd, string Status,
    DateTimeOffset? CheckInAt, DateTimeOffset? CheckOutAt, string? CheckOutType, DateTimeOffset? OpenExitAt, DateTimeOffset? OpenExitReturnBy,
    int LateUnexcused, int EarlyUnexcused, int NetWorkMinutes, int OvertimeMinutes, bool HasPendingException,
    IReadOnlyList<PermissionDto> Permissions);

public sealed record MyScheduleDayDto(DateOnly Date, string LocationNameAr, string LocationNameEn, string ShiftNameAr, string ShiftNameEn,
    DateTimeOffset Start, DateTimeOffset End, bool IsOnLeave, double Latitude, double Longitude);

[ApiController]
[Route("api/me")]
[Authorize(Policy = Policies.SelfService)]
public sealed class MyAttendanceController(
    AppDbContext db, ICurrentUser me, IClock clock, RecalculationService recalculator, PermissionService permissions,
    ScheduleSnapshotLoader loader, IOptions<AttendanceOptions> options) : ControllerBase
{
    [HttpGet("today")]
    public async Task<IReadOnlyList<TodayShiftDto>> Today(CancellationToken ct)
    {
        var now = clock.Now;
        var records = (await CandidateRecords(tracking: false, ct))
            .Where(r => r.ScheduledEnd > now.AddHours(-12) || r.IsOpen)
            .OrderBy(r => r.ScheduledStart).ToList();

        var locationIds = records.Select(r => r.LocationId).ToList();
        var shiftIds = records.Select(r => r.ShiftTemplateId).ToList();
        var locations = await db.Locations.AsNoTracking().Where(l => locationIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var shifts = await db.ShiftTemplates.AsNoTracking().Where(s => shiftIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var dates = records.Select(r => r.ShiftDate).Distinct().ToList();
        var perms = await db.PermissionRequests.AsNoTracking()
            .Where(p => p.EmployeeId == me.RequiredId && dates.Contains(p.ShiftDate) && p.IsActive).ToListAsync(ct);
        var recordIds = records.Select(r => r.Id).ToList();
        var pending = (await db.AttendanceExceptionRequests.AsNoTracking()
            .Where(e => recordIds.Contains(e.AttendanceRecordId) && e.Status == RequestStatus.Pending)
            .Select(e => e.AttendanceRecordId).ToListAsync(ct)).ToHashSet();

        return records.Select(r =>
        {
            var l = locations[r.LocationId];
            var s = shifts[r.ShiftTemplateId];
            var open = r.OpenExit;
            return new TodayShiftDto(r.Id, r.ShiftDate, l.NameAr, l.NameEn, l.Latitude, l.Longitude, l.RadiusMeters, s.NameAr, s.NameEn,
                r.ScheduledStart, r.ScheduledEnd, r.Status.ToString(), r.CheckInAt, r.CheckOutAt, r.CheckOutType?.ToString(),
                open?.ExitAt, open?.PermissionEnd, r.LateUnexcused, r.EarlyUnexcused, r.NetWorkMinutes, r.OvertimeMinutes,
                pending.Contains(r.Id),
                perms.Where(p => p.ShiftDate == r.ShiftDate && RecalculationService.TryInterval(p, r.ScheduledWindow) is not null)
                     .Select(ToDto).ToList());
        }).ToList();
    }

    [HttpGet("schedule")]
    public async Task<IReadOnlyList<MyScheduleDayDto>> Schedule(CancellationToken ct)
    {
        var from = clock.Today;
        var to = from.AddDays(13);
        var snapshot = await loader.LoadAsync([me.RequiredId], from, to, ct);
        var locations = await db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, ct);
        return ScheduleResolver.ResolveRange(me.RequiredId, from, to, snapshot).Select(s =>
        {
            var l = locations[s.LocationId];
            var t = snapshot.ShiftTemplates[s.ShiftTemplateId];
            return new MyScheduleDayDto(s.ShiftDate, l.NameAr, l.NameEn, t.NameAr, t.NameEn, s.Window.Start, s.Window.End, s.IsOnLeave, l.Latitude, l.Longitude);
        }).ToList();
    }

    [HttpPost("check-in")]
    public async Task<IActionResult> CheckIn(GpsFix fix, CancellationToken ct)
    {
        var now = clock.Now;
        var record = AttendancePolicies.SelectRecordForCheckIn(await CandidateRecords(tracking: true, ct), now)
            ?? throw new DomainException("attendance.no_shift", "You have no shift to check in to right now.");

        var (geo, rejection) = await EvaluateAsync(record.LocationId, fix, ct);
        if (rejection is not null) return rejection;

        record.CheckIn(now, new GeoPoint(fix.Latitude, fix.Longitude), fix.Accuracy, geo.DistanceMeters, CheckInType.Normal);
        await recalculator.RecalculateAsync(record, ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("check-out")]
    public async Task<IActionResult> CheckOut(GpsFix fix, CancellationToken ct)
    {
        var record = await OpenRecord(ct);
        var (_, rejection) = await EvaluateAsync(record.LocationId, fix, ct);
        if (rejection is not null) return rejection;

        record.CheckOut(clock.Now, new GeoPoint(fix.Latitude, fix.Longitude), CheckOutType.Normal);
        await recalculator.RecalculateAsync(record, ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Leaving on an approved temporary-exit permission: no geofence needed (spec 3.5).</summary>
    [HttpPost("exit")]
    public async Task<IActionResult> StartExit(ExitRequest r, CancellationToken ct)
    {
        var record = await OpenRecord(ct);
        var permission = await db.PermissionRequests.AsNoTracking().SingleOrDefaultAsync(p =>
                p.Id == r.PermissionId && p.EmployeeId == me.RequiredId && p.Type == PermissionType.TemporaryExit
                && p.Status == RequestStatus.Approved && p.ShiftDate == record.ShiftDate, ct)
            ?? throw new DomainException("permission.not_found", "No approved temporary-exit permission found.");

        record.StartTemporaryExit(clock.Now, permission.Id, permission.ToInterval(record.ScheduledWindow));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("return")]
    public async Task<IActionResult> Return(GpsFix fix, CancellationToken ct)
    {
        var record = await OpenRecord(ct);
        var (_, rejection) = await EvaluateAsync(record.LocationId, fix, ct);
        if (rejection is not null) return rejection;

        record.ReturnFromExit(clock.Now, new GeoPoint(fix.Latitude, fix.Longitude));
        await recalculator.RecalculateAsync(record, ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("exception")]
    public async Task<IActionResult> RequestException(ExceptionSubmit r, CancellationToken ct)
    {
        if (!Enum.TryParse<ExceptionKind>(r.Kind, true, out var kind))
            throw new DomainException("exception.invalid_kind", "Unknown exception kind.");

        var now = clock.Now;
        var record = kind == ExceptionKind.CheckIn
            ? AttendancePolicies.SelectRecordForCheckIn(await CandidateRecords(tracking: false, ct), now)
              ?? throw new DomainException("attendance.no_shift", "You have no shift to check in to right now.")
            : await OpenRecord(ct);

        if (await db.AttendanceExceptionRequests.AnyAsync(e => e.AttendanceRecordId == record.Id && e.Kind == kind && e.Status == RequestStatus.Pending, ct))
            throw new DomainException("exception.already_pending", "You already have a pending request for this.");

        var location = await db.Locations.AsNoTracking().SingleAsync(l => l.Id == record.LocationId, ct);
        var distance = GeoDistance.Meters(location.Point, new GeoPoint(r.Latitude, r.Longitude));
        db.AttendanceExceptionRequests.Add(new AttendanceExceptionRequest(me.RequiredId, record.Id, kind, now,
            r.Latitude, r.Longitude, Math.Round(distance, 1), r.Reason));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("exception/{id:guid}/cancel")]
    public async Task<IActionResult> CancelException(Guid id, CancellationToken ct)
    {
        var e = await db.AttendanceExceptionRequests.SingleOrDefaultAsync(x => x.Id == id && x.EmployeeId == me.RequiredId, ct)
            ?? throw new DomainException("request.not_found", "Request not found.");
        e.Cancel();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("permissions")]
    public async Task<IReadOnlyList<PermissionDto>> MyPermissions(CancellationToken ct) =>
        (await db.PermissionRequests.AsNoTracking().Where(p => p.EmployeeId == me.RequiredId && p.IsActive)
            .OrderByDescending(p => p.CreatedAt).Take(100).ToListAsync(ct)).Select(ToDto).ToList();

    [HttpPost("permissions")]
    public async Task<ActionResult<PermissionDto>> SubmitPermission(SubmitPermissionRequest r, CancellationToken ct) =>
        ToDto(await permissions.SubmitAsync(me.RequiredId, me.RequiredId, r, ct));

    [HttpPost("permissions/{id:guid}/cancel")]
    public async Task<IActionResult> CancelPermission(Guid id, CancellationToken ct)
    {
        await permissions.CancelAsync(id, me.RequiredId, ct);
        return NoContent();
    }

    internal static PermissionDto ToDto(PermissionRequest p) =>
        new(p.Id, p.Type.ToString(), p.ShiftDate, p.FromTime, p.ToTime, p.Status.ToString(), p.Reason, p.RejectReason, p.CreatedAt);

    /// <summary>Yesterday's records are included so a night shift crossing midnight is found.</summary>
    private async Task<List<AttendanceRecord>> CandidateRecords(bool tracking, CancellationToken ct)
    {
        var today = clock.Today;
        var query = db.AttendanceRecords.Include(r => r.Exits)
            .Where(r => r.EmployeeId == me.RequiredId && r.ShiftDate >= today.AddDays(-1) && r.ShiftDate <= today);
        return await (tracking ? query : query.AsNoTracking()).ToListAsync(ct);
    }

    private async Task<AttendanceRecord> OpenRecord(CancellationToken ct) =>
        (await CandidateRecords(tracking: true, ct)).FirstOrDefault(r => r.IsOpen)
        ?? throw new DomainException("attendance.not_present", "You are not checked in.");

    /// <returns>422 with { code, data: { outcome, distance, accuracy, radius } } when rejected, so the app can offer an exception request.</returns>
    private async Task<(GeofenceResult Result, ObjectResult? Rejection)> EvaluateAsync(Guid locationId, GpsFix fix, CancellationToken ct)
    {
        var location = await db.Locations.AsNoTracking().SingleAsync(l => l.Id == locationId, ct);
        var result = GeofencePolicy.Evaluate(location.Point, location.RadiusMeters, new GeoPoint(fix.Latitude, fix.Longitude),
            fix.Accuracy, options.Value.MaxGpsAccuracyMeters);
        if (result.Accepted) return (result, null);

        var code = result.Outcome == GeofenceOutcome.LowAccuracy ? "geofence.low_accuracy" : "geofence.outside";
        var body = new ApiError(code, "Location check failed.",
            new { outcome = result.Outcome.ToString(), distance = result.DistanceMeters, accuracy = fix.Accuracy, radius = location.RadiusMeters });
        return (result, UnprocessableEntity(body));
    }
}
