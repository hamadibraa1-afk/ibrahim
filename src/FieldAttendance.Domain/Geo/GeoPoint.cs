using FieldAttendance.Domain.Common;

namespace FieldAttendance.Domain.Geo;

public readonly record struct GeoPoint
{
    public GeoPoint(double latitude, double longitude)
    {
        if (double.IsNaN(latitude) || latitude is < -90 or > 90)
            throw new DomainException("geo.invalid_latitude", "Latitude must be between -90 and 90.");
        if (double.IsNaN(longitude) || longitude is < -180 or > 180)
            throw new DomainException("geo.invalid_longitude", "Longitude must be between -180 and 180.");
        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }
    public double Longitude { get; }
}

public static class GeoDistance
{
    private const double EarthRadiusMeters = 6_371_000d;

    /// <summary>Great-circle distance in meters (Haversine).</summary>
    public static double Meters(GeoPoint a, GeoPoint b)
    {
        var dLat = ToRadians(b.Latitude - a.Latitude);
        var dLon = ToRadians(b.Longitude - a.Longitude);
        var h = Math.Pow(Math.Sin(dLat / 2), 2) +
                Math.Cos(ToRadians(a.Latitude)) * Math.Cos(ToRadians(b.Latitude)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}
