using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Geo;

namespace FieldAttendance.Domain.Entities;

public sealed class Location : Entity
{
    public const int MinRadiusMeters = 20;
    public const int MaxRadiusMeters = 2000;

    private Location() { } // EF Core

    public Location(string nameAr, string nameEn, string? address, GeoPoint point, int radiusMeters, int capacity, DateTimeOffset now,
        LocationKind kind = LocationKind.Field)
    {
        Kind = kind;
        Rename(nameAr, nameEn, address);
        SetGeofence(point, radiusMeters);
        SetCapacity(capacity);
        RegenerateQr(now);
    }

    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public string? Address { get; private set; }
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public int RadiusMeters { get; private set; }
    public int Capacity { get; private set; } = 1;

    /// <summary>A field site collectors are rotated across, or an office branch with fixed staff.</summary>
    public LocationKind Kind { get; private set; } = LocationKind.Field;
    public Guid QrToken { get; private set; }
    public DateTimeOffset QrGeneratedAt { get; private set; }

    public GeoPoint Point => new(Latitude, Longitude);

    public void Rename(string nameAr, string nameEn, string? address)
    {
        NameAr = Guard.Required(nameAr, "location.name_ar", 150);
        NameEn = Guard.Required(nameEn, "location.name_en", 150);
        Address = Guard.Optional(address, "location.address", 300);
    }

    public void SetGeofence(GeoPoint point, int radiusMeters)
    {
        Latitude = point.Latitude;
        Longitude = point.Longitude;
        RadiusMeters = Guard.InRange(radiusMeters, MinRadiusMeters, MaxRadiusMeters, "location.radius");
    }

    public void SetCapacity(int capacity) => Capacity = Guard.InRange(capacity, 1, 5000, "location.capacity");

    public void SetKind(LocationKind kind) => Kind = kind;

    /// <summary>Invalidates every printed code for this location immediately.</summary>
    public void RegenerateQr(DateTimeOffset now)
    {
        QrToken = Guid.NewGuid();
        QrGeneratedAt = now;
    }

    public bool OverlapsGeofence(Location other) =>
        other.Id != Id && GeoDistance.Meters(Point, other.Point) < RadiusMeters + other.RadiusMeters;
}
