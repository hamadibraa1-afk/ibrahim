using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Scheduling;

namespace FieldAttendance.Domain.Tests;

internal static class Builders
{
    public static readonly Guid Employee = Guid.NewGuid();
    public static readonly Guid OtherEmployee = Guid.NewGuid();
    public static readonly Guid LocationA = Guid.NewGuid();
    public static readonly Guid LocationB = Guid.NewGuid();

    public static readonly WorkDays SundayToThursday =
        WorkDays.Sunday | WorkDays.Monday | WorkDays.Tuesday | WorkDays.Wednesday | WorkDays.Thursday;

    public static ShiftTemplate Morning() =>
        new("صباحي", "Morning", new TimeOnly(8, 0), new TimeOnly(16, 0), 0, 10, false, earlyCheckInMinutes: 60);

    public static ShiftTemplate Night() =>
        new("ليلي", "Night", new TimeOnly(22, 0), new TimeOnly(6, 0), 0, 10, false);

    public static ShiftTemplate Evening() =>
        new("مسائي", "Evening", new TimeOnly(16, 0), new TimeOnly(22, 0), 0, 10, false);

    public static ScheduleSnapshot Snapshot(
        IEnumerable<ShiftTemplate> templates,
        IEnumerable<Assignment>? assignments = null,
        IEnumerable<AssignmentOverride>? overrides = null,
        IEnumerable<LeaveRequest>? leaves = null) =>
        new(assignments?.ToList() ?? [], overrides?.ToList() ?? [], leaves?.ToList() ?? [],
            templates.ToDictionary(t => t.Id));

    public static T Created<T>(this T entity, int minutesAfterDay0) where T : Common.Entity
    {
        entity.CreatedAt = TestTime.T(0, 0).AddMinutes(minutesAfterDay0);
        return entity;
    }

    public static AttendanceRecord Record(ShiftTemplate? template = null, bool onLeave = false)
    {
        var t = template ?? Morning();
        return new AttendanceRecord(new ResolvedShift(Employee, TestTime.Day0, LocationA, t.Id, t.WindowFor(TestTime.Day0),
            t.BreakMinutes, t.GraceMinutes, t.CountEarlyArrivalAsOvertime, t.EarlyCheckInMinutes, ShiftSource.BaseAssignment, Guid.NewGuid(), null, onLeave));
    }
}
