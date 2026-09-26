using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Time;

namespace FieldAttendance.Domain.Attendance;

public static class AttendancePolicies
{
    /// <summary>
    /// Which record a check-in applies to when the employee has several shifts around now
    /// (split shifts, or yesterday's shift crossing midnight): the earliest shift that
    /// has not ended, is not on leave and has no check-in yet.
    /// </summary>
    public static AttendanceRecord? SelectRecordForCheckIn(IEnumerable<AttendanceRecord> candidates, DateTimeOffset now) =>
        candidates
            .Where(r => r.IsActive && !r.HasCheckedIn && r.Status != AttendanceStatus.OnLeave && now < r.ScheduledEnd)
            .OrderBy(r => r.ScheduledStart)
            .FirstOrDefault();

    /// <summary>
    /// "Not arrived yet" alert (spec 3.7). With an approved late permission, the alert waits
    /// until the permission ends. Returns the instant the alert becomes due, or null if none applies.
    /// </summary>
    public static DateTimeOffset? NotArrivedAlertDueAt(AttendanceRecord record, IEnumerable<TimeInterval> approvedLateWindows, int alertDelayMinutes)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.HasCheckedIn || record.Status != AttendanceStatus.Scheduled)
            return null;

        var expected = record.LateAfter;
        foreach (var window in approvedLateWindows)
        {
            if (window.End > expected) expected = window.End;
        }

        var due = expected.AddMinutes(alertDelayMinutes);
        return due < record.ScheduledEnd ? due : null;
    }
}
