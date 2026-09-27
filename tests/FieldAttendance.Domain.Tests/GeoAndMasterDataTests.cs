using FieldAttendance.Domain.Attendance;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Geo;
using Xunit;
using static FieldAttendance.Domain.Tests.TestTime;

namespace FieldAttendance.Domain.Tests;

public class GeoAndMasterDataTests
{
    private static readonly GeoPoint Site = new(25.3463, 55.4209);

    // 0.001° of latitude ≈ 111 m
    private static readonly GeoPoint About111mNorth = new(25.3473, 55.4209);

    [Fact]
    public void Haversine_IsAccurate() =>
        Assert.InRange(GeoDistance.Meters(Site, About111mNorth), 110.0, 112.5);

    [Fact]
    public void Inside_WithGoodAccuracy_IsAccepted() =>
        Assert.True(GeofencePolicy.Evaluate(Site, 150, About111mNorth, 20, 100).Accepted);

    [Fact]
    public void Outside_IsRejectedWithDistance()
    {
        var r = GeofencePolicy.Evaluate(Site, 50, About111mNorth, 20, 100);
        Assert.Equal(GeofenceOutcome.Outside, r.Outcome);
        Assert.InRange(r.DistanceMeters, 110.0, 112.5);
    }

    [Fact]
    public void PoorAccuracy_IsRejectedEvenWhenInside() =>
        Assert.Equal(GeofenceOutcome.LowAccuracy, GeofencePolicy.Evaluate(Site, 150, Site, 250, 100).Outcome);

    [Fact]
    public void InvalidAccuracy_IsRejected() =>
        Assert.Equal(GeofenceOutcome.LowAccuracy, GeofencePolicy.Evaluate(Site, 150, Site, double.NaN, 100).Outcome);

    [Fact]
    public void InvalidCoordinates_AreRejected() =>
        Assert.Throws<DomainException>(() => new GeoPoint(95, 55));

    [Fact]
    public void Location_RadiusBelowMinimum_IsRejected() =>
        Assert.Throws<DomainException>(() => new Location("أ", "A", null, Site, 5, 1, T(8, 0)));

    [Fact]
    public void Location_RegenerateQr_InvalidatesOldToken()
    {
        var loc = new Location("أ", "A", null, Site, 100, 1, T(8, 0));
        var old = loc.QrToken;
        loc.RegenerateQr(T(9, 0));
        Assert.NotEqual(old, loc.QrToken);
    }

    [Fact]
    public void Location_OverlappingGeofences_AreDetected()
    {
        var a = new Location("أ", "A", null, Site, 100, 1, T(8, 0));
        var b = new Location("ب", "B", null, About111mNorth, 100, 1, T(8, 0));
        var c = new Location("ج", "C", null, new GeoPoint(25.36, 55.4209), 100, 1, T(8, 0));
        Assert.True(a.OverlapsGeofence(b));
        Assert.False(a.OverlapsGeofence(c));
    }

    [Fact]
    public void Shift_EqualStartAndEnd_IsRejected() =>
        Assert.Throws<DomainException>(() => new ShiftTemplate("x", "x", new TimeOnly(8, 0), new TimeOnly(8, 0), 0, 0, false));

    [Fact]
    public void Shift_BreakLongerThanShift_IsRejected() =>
        Assert.Throws<DomainException>(() => new ShiftTemplate("x", "x", new TimeOnly(8, 0), new TimeOnly(9, 0), 60, 0, false));

    [Fact]
    public void Shift_NightDuration_IsComputedAcrossMidnight() =>
        Assert.Equal(480, new ShiftTemplate("x", "x", new TimeOnly(22, 0), new TimeOnly(6, 0), 0, 0, false).DurationMinutes);

    [Fact]
    public void Shift_NightBreak_IsValidatedAgainstRealDuration() =>
        Assert.Throws<DomainException>(() => new ShiftTemplate("x", "x", new TimeOnly(22, 0), new TimeOnly(6, 0), 480, 0, false));

    [Fact]
    public void Phone_IsNormalizedToE164()
    {
        var user = new User("Ali", "ali@example.com", "050 123 4567", UserRole.Collector, "2001", "ar");
        Assert.Equal("+971501234567", user.Phone);
    }

    [Fact]
    public void EveryAccount_RequiresEmployeeNumber_BecauseItIsTheLogin() =>
        Assert.Throws<DomainException>(() => new User("Ali", "ali@example.com", "0501234567", UserRole.Collector, " ", "ar"));

    [Fact]
    public void Email_IsOptional() =>
        Assert.Null(new User("Sara", null, "0501234567", UserRole.DepartmentManager, "1003", "en").Email);

    [Fact]
    public void InvalidEmail_IsRejected() =>
        Assert.Throws<DomainException>(() => new User("Ali", "not-an-email", "0501234567", UserRole.Supervisor, "1002", "ar"));
}

public class WorkforceTests
{
    private static Entities.EmployeeProfile Profile(Enums.Workforce workforce) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2025, 1, 1), workforce);

    [Fact]
    public void A_profile_is_office_staff_unless_said_otherwise() =>
        Assert.Equal(Enums.Workforce.Office, new Entities.EmployeeProfile(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2025, 1, 1)).Workforce);

    [Fact]
    public void Field_staff_cannot_be_given_an_office_work_schedule()
    {
        var ex = Assert.Throws<Common.DomainException>(() => Profile(Enums.Workforce.Field).SetSchedule(Guid.NewGuid()));
        Assert.Equal("profile.field_schedule", ex.Code);
    }

    [Fact]
    public void Moving_someone_to_the_field_drops_their_office_schedule()
    {
        var profile = Profile(Enums.Workforce.Office);
        profile.SetSchedule(Guid.NewGuid());

        profile.SetWorkforce(Enums.Workforce.Field);

        Assert.Null(profile.WorkScheduleId);
    }
}
