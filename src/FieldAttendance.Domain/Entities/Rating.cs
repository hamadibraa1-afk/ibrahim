using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;

namespace FieldAttendance.Domain.Entities;

public sealed class Rating : Entity
{
    private Rating() { } // EF Core

    public Rating(Guid locationId, Guid? employeeId, Guid? attendanceRecordId, int stars, string? comment,
        DateTimeOffset scannedAt, string deviceToken, string ipHash, RatingLinkType linkType)
    {
        LocationId = Guard.NotEmpty(locationId, "rating.location");
        EmployeeId = employeeId;
        AttendanceRecordId = attendanceRecordId;
        Stars = Guard.InRange(stars, 1, 5, "rating.stars");
        Comment = Guard.Optional(comment, "rating.comment", 500);
        ScannedAt = scannedAt;
        DeviceToken = Guard.Required(deviceToken, "rating.device", 100);
        IpHash = Guard.Required(ipHash, "rating.ip", 100);
        LinkType = linkType;
        IncludedInAverage = linkType != RatingLinkType.OutsideShift;
    }

    public Guid LocationId { get; private set; }
    public Guid? EmployeeId { get; private set; }
    public Guid? AttendanceRecordId { get; private set; }
    public int Stars { get; private set; }
    public string? Comment { get; private set; }
    public DateTimeOffset ScannedAt { get; private set; }
    public string DeviceToken { get; private set; } = string.Empty;
    public string IpHash { get; private set; } = string.Empty;
    public RatingLinkType LinkType { get; private set; }
    public bool IncludedInAverage { get; private set; }

    public void SetIncludedInAverage(bool included) => IncludedInAverage = included;
}
