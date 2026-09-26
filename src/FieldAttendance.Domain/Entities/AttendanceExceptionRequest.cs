using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;

namespace FieldAttendance.Domain.Entities;

/// <summary>
/// Raised when geofence validation fails. On approval, the check-in/out is recorded
/// at <see cref="RequestedAt"/> (the moment the employee asked), not the approval time.
/// </summary>
public sealed class AttendanceExceptionRequest : ApprovableRequest
{
    private AttendanceExceptionRequest() { } // EF Core

    public AttendanceExceptionRequest(Guid employeeId, Guid attendanceRecordId, ExceptionKind kind, DateTimeOffset requestedAt,
        double latitude, double longitude, double distanceMeters, string reason)
        : base(employeeId, reason, employeeId, reasonRequired: true)
    {
        AttendanceRecordId = Guard.NotEmpty(attendanceRecordId, "exception.record");
        Kind = kind;
        RequestedAt = requestedAt;
        Latitude = latitude;
        Longitude = longitude;
        DistanceMeters = distanceMeters;
    }

    public Guid AttendanceRecordId { get; private set; }
    public ExceptionKind Kind { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public double DistanceMeters { get; private set; }

    public void Cancel() => MarkCancelled(allowApproved: false);
}
