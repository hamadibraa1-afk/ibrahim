using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Time;

namespace FieldAttendance.Domain.Scheduling;

public enum ShiftSource { BaseAssignment = 1, Override = 2 }

/// <summary>
/// The effective shift for one employee on one date, with a snapshot of the
/// shift rules at resolution time. Attendance records are materialized from this.
/// </summary>
public sealed record ResolvedShift(
    Guid EmployeeId,
    DateOnly ShiftDate,
    Guid LocationId,
    Guid ShiftTemplateId,
    TimeInterval Window,
    int BreakMinutes,
    int GraceMinutes,
    bool CountEarlyArrivalAsOvertime,
    int EarlyCheckInMinutes,
    ShiftSource Source,
    Guid SourceId,
    OverrideType? OverrideType,
    bool IsOnLeave,
    int FlexMinutes = 0);
