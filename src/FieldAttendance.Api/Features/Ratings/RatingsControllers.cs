using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Ratings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FieldAttendance.Api.Features.Ratings;

public sealed record PublicEmployeeDto(Guid Id, string FirstName);

public sealed record PublicLocationDto(string NameAr, string NameEn, IReadOnlyList<PublicEmployeeDto> ChooseEmployee);

public sealed record SubmitRatingRequest([Range(1, 5)] int Stars, [MaxLength(500)] string? Comment, Guid? EmployeeId,
    [Required, MaxLength(100)] string DeviceToken);

public sealed record RatingDto(Guid Id, DateTimeOffset ScannedAt, string LocationName, string? EmployeeName, int Stars,
    string? Comment, string LinkType, bool IncludedInAverage);

public sealed record EmployeeAverageDto(Guid EmployeeId, string EmployeeName, double Average, int Count);

public sealed record RatingsPageDto(double? Average, int Count, IReadOnlyList<EmployeeAverageDto> ByEmployee, IReadOnlyList<RatingDto> Items);

public sealed record IncludeRequest(bool Included);

/// <summary>
/// Anonymous QR endpoints. Never reveal whether anyone is on duty: the response only lists
/// employees when the customer must choose between two or more.
/// </summary>
[ApiController]
[Route("api/public/r/{token:guid}")]
[AllowAnonymous]
[EnableRateLimiting("public")]
public sealed class PublicRatingController(AppDbContext db, IClock clock, IOptions<AttendanceOptions> options) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PublicLocationDto>> Get(Guid token, CancellationToken ct)
    {
        var location = await FindLocation(token, ct);
        var present = await PresentAsync(location.Id, ct);
        var choose = present.Count > 1
            ? present.Select(p => new PublicEmployeeDto(p.EmployeeId, FirstName(p.DisplayName))).ToList()
            : [];
        return new PublicLocationDto(location.NameAr, location.NameEn, choose);
    }

    [HttpPost]
    public async Task<IActionResult> Submit(Guid token, SubmitRatingRequest r, CancellationToken ct)
    {
        var location = await FindLocation(token, ct);
        var now = clock.Now;

        var repeatSince = now.AddMinutes(-options.Value.RatingRepeatWindowMinutes);
        if (await db.Ratings.AnyAsync(x => x.LocationId == location.Id && x.DeviceToken == r.DeviceToken && x.ScannedAt >= repeatSince, ct))
            return Conflict(new ApiError("rating.already_submitted", "You have already rated this visit."));

        var present = await PresentAsync(location.Id, ct);
        var withinShift = await db.AttendanceRecords.AnyAsync(x => x.LocationId == location.Id && x.ScheduledStart <= now && x.ScheduledEnd > now, ct);
        var link = RatingLinkPolicy.Decide(present, r.EmployeeId, withinShift);

        db.Ratings.Add(new Rating(location.Id, link.EmployeeId, link.AttendanceRecordId, r.Stars, r.Comment, now,
            r.DeviceToken, HashIp(HttpContext.Connection.RemoteIpAddress?.ToString()), link.LinkType));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Office branches have no customer-facing code: rating and complaints are a field-site idea.
    private async Task<Location> FindLocation(Guid token, CancellationToken ct) =>
        await db.Locations.AsNoTracking()
            .SingleOrDefaultAsync(l => l.QrToken == token && l.IsActive && l.Kind == LocationKind.Field, ct)
        ?? throw new DomainException("rating.invalid_code", "This code is not valid.");

    /// <summary>Checked in, not checked out, and not on a temporary exit.</summary>
    private async Task<List<PresentEmployee>> PresentAsync(Guid locationId, CancellationToken ct)
    {
        var onExit = db.TemporaryExits.Where(e => e.ReturnAt == null).Select(e => e.AttendanceRecordId);
        return await db.AttendanceRecords.AsNoTracking()
            .Where(r => r.LocationId == locationId && r.CheckInAt != null && r.CheckOutAt == null && !onExit.Contains(r.Id))
            .Join(db.Users, r => r.EmployeeId, u => u.Id, (r, u) => new PresentEmployee(u.Id, r.Id, u.FullName))
            .ToListAsync(ct);
    }

    private static string FirstName(string fullName) => fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? fullName;

    private static string HashIp(string? ip) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ip ?? "unknown")))[..32];
}

[ApiController]
[Route("api/ratings")]
[Authorize(Policy = Policies.Read)]
public sealed class RatingsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<RatingsPageDto> List([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? locationId,
        [FromQuery] Guid? employeeId, CancellationToken ct)
    {
        var start = Domain.Time.UaeTime.At(from, TimeOnly.MinValue);
        var end = Domain.Time.UaeTime.At(to.AddDays(1), TimeOnly.MinValue);
        var q = db.Ratings.AsNoTracking().Where(r => r.ScannedAt >= start && r.ScannedAt < end);
        if (locationId is { } l) q = q.Where(r => r.LocationId == l);
        if (employeeId is { } e) q = q.Where(r => r.EmployeeId == e);

        var items = await q.OrderByDescending(r => r.ScannedAt).Take(2000).ToListAsync(ct);
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var locations = await db.Locations.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.NameAr, ct);
        var counted = items.Where(r => r.IncludedInAverage).ToList();

        var byEmployee = counted.Where(r => r.EmployeeId != null).GroupBy(r => r.EmployeeId!.Value)
            .Select(g => new EmployeeAverageDto(g.Key, users.GetValueOrDefault(g.Key, "?"), Math.Round(g.Average(r => r.Stars), 2), g.Count()))
            .OrderByDescending(x => x.Average).ThenByDescending(x => x.Count).ToList();

        return new RatingsPageDto(
            counted.Count > 0 ? Math.Round(counted.Average(r => r.Stars), 2) : null, counted.Count, byEmployee,
            items.Select(r => new RatingDto(r.Id, r.ScannedAt, locations.GetValueOrDefault(r.LocationId, "?"),
                r.EmployeeId is { } id ? users.GetValueOrDefault(id) : null, r.Stars, r.Comment, r.LinkType.ToString(), r.IncludedInAverage)).ToList());
    }

    [HttpPost("{id:guid}/include")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<IActionResult> SetIncluded(Guid id, IncludeRequest r, CancellationToken ct)
    {
        var rating = await db.Ratings.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new DomainException("rating.not_found", "Rating not found.");
        rating.SetIncludedInAverage(r.Included);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
