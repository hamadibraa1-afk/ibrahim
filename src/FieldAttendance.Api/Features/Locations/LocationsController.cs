using System.ComponentModel.DataAnnotations;
using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Geo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Locations;

public sealed record LocationDto(Guid Id, string NameAr, string NameEn, string? Address, double Latitude, double Longitude,
    int RadiusMeters, int Capacity, Guid QrToken, string Kind, bool IsActive, IReadOnlyList<string> OverlapsWith, byte[] RowVersion);

public sealed record SaveLocationRequest(
    [Required] string NameAr, [Required] string NameEn, string? Address,
    [Range(-90, 90)] double Latitude, [Range(-180, 180)] double Longitude,
    [Range(Location.MinRadiusMeters, Location.MaxRadiusMeters)] int RadiusMeters,
    [Range(1, 5000)] int Capacity, string? Kind, byte[]? RowVersion);

[ApiController]
[Route("api/locations")]
[Authorize(Policy = Policies.Read)]
public sealed class LocationsController(AppDbContext db, IClock clock, AccessScope scope) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<LocationDto>> List([FromQuery] bool includeInactive, [FromQuery] string? kind, CancellationToken ct)
    {
        var all = await db.Locations.AsNoTracking().OrderBy(l => l.NameAr).ToListAsync(ct);
        var active = all.Where(l => l.IsActive).ToList();
        var scoped = await scope.LocationIdsAsync(ct);
        if (scoped is not null) all = all.Where(l => scoped.Contains(l.Id)).ToList();
        if (Enum.TryParse<LocationKind>(kind, true, out var wanted)) all = all.Where(l => l.Kind == wanted).ToList();
        return all.Where(l => includeInactive || l.IsActive)
                  .Select(l => ToDto(l, active))
                  .ToList();
    }

    [HttpPost]
    [Authorize(Policy = Policies.Manage)]
    public async Task<ActionResult<LocationDto>> Create(SaveLocationRequest r, CancellationToken ct)
    {
        var location = new Location(r.NameAr, r.NameEn, r.Address, new GeoPoint(r.Latitude, r.Longitude), r.RadiusMeters,
            r.Capacity, clock.Now, Enum.TryParse<LocationKind>(r.Kind, true, out var kind) ? kind : LocationKind.Field);
        db.Locations.Add(location);
        await db.SaveChangesAsync(ct);
        return ToDto(location, await ActiveAsync(ct));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<ActionResult<LocationDto>> Update(Guid id, SaveLocationRequest r, CancellationToken ct)
    {
        var location = await Find(id, ct);
        db.ExpectVersion(location, r.RowVersion);
        location.Rename(r.NameAr, r.NameEn, r.Address);
        location.SetGeofence(new GeoPoint(r.Latitude, r.Longitude), r.RadiusMeters);
        location.SetCapacity(r.Capacity);
        if (Enum.TryParse<LocationKind>(r.Kind, true, out var kind)) location.SetKind(kind);
        await db.SaveChangesAsync(ct);
        return ToDto(location, await ActiveAsync(ct));
    }

    /// <summary>Soft delete: the location disappears from lists but its history and ratings stay intact.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<ActionResult<LocationDto>> Delete(Guid id, CancellationToken ct)
    {
        var location = await Find(id, ct);
        var today = clock.Today;
        var inUse = await db.Assignments.AnyAsync(a => a.LocationId == id && a.IsActive && (a.EndDate == null || a.EndDate >= today), ct)
                 || await db.AssignmentOverrides.AnyAsync(o => o.LocationId == id && o.IsActive && o.ToDate >= today, ct);
        if (inUse)
            throw new DomainException("location.in_use", "This location has current or future assignments. End them first.");
        location.Deactivate();
        await db.SaveChangesAsync(ct);
        return ToDto(location, await ActiveAsync(ct));
    }

    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<ActionResult<LocationDto>> Restore(Guid id, CancellationToken ct)
    {
        var location = await Find(id, ct);
        location.Activate();
        await db.SaveChangesAsync(ct);
        return ToDto(location, await ActiveAsync(ct));
    }

    [HttpPost("{id:guid}/regenerate-qr")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<ActionResult<LocationDto>> RegenerateQr(Guid id, CancellationToken ct)
    {
        var location = await Find(id, ct);
        location.RegenerateQr(clock.Now);
        await db.SaveChangesAsync(ct);
        return ToDto(location, await ActiveAsync(ct));
    }

    private async Task<Location> Find(Guid id, CancellationToken ct) =>
        await db.Locations.SingleOrDefaultAsync(l => l.Id == id, ct)
        ?? throw new DomainException("location.not_found", "Location not found.");

    private async Task<List<Location>> ActiveAsync(CancellationToken ct) =>
        await db.Locations.AsNoTracking().Where(l => l.IsActive).ToListAsync(ct);

    private static LocationDto ToDto(Location l, IEnumerable<Location> active) =>
        new(l.Id, l.NameAr, l.NameEn, l.Address, l.Latitude, l.Longitude, l.RadiusMeters, l.Capacity, l.QrToken, l.Kind.ToString(), l.IsActive,
            active.Where(o => l.OverlapsGeofence(o)).Select(o => o.NameAr).ToList(), l.RowVersion);
}
