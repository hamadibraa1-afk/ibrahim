using FieldAttendance.Domain.Geo;

namespace FieldAttendance.Domain.Attendance;

public enum GeofenceOutcome { Inside = 1, Outside = 2, LowAccuracy = 3 }

public sealed record GeofenceResult(GeofenceOutcome Outcome, double DistanceMeters, double AccuracyMeters)
{
    public bool Accepted => Outcome == GeofenceOutcome.Inside;
}

/// <summary>
/// Server-side only. Accuracy is checked first: a fix worse than the threshold is
/// rejected even if it lands inside, because the true position could be anywhere in that radius.
/// </summary>
public static class GeofencePolicy
{
    public static GeofenceResult Evaluate(GeoPoint site, int radiusMeters, GeoPoint device, double accuracyMeters, double maxAccuracyMeters)
    {
        var distance = Math.Round(GeoDistance.Meters(site, device), 1);

        if (double.IsNaN(accuracyMeters) || accuracyMeters <= 0 || accuracyMeters > maxAccuracyMeters)
            return new GeofenceResult(GeofenceOutcome.LowAccuracy, distance, accuracyMeters);

        return new GeofenceResult(distance <= radiusMeters ? GeofenceOutcome.Inside : GeofenceOutcome.Outside, distance, accuracyMeters);
    }
}
