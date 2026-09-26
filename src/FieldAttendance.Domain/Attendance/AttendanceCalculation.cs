using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Time;

namespace FieldAttendance.Domain.Attendance;

/// <param name="ExitAt">When the employee pressed "exit with permission".</param>
/// <param name="ReturnAt">When they returned; null if they never came back.</param>
/// <param name="PermissionWindow">The approved temporary-exit window (From → To).</param>
public sealed record ExitSpan(DateTimeOffset ExitAt, DateTimeOffset? ReturnAt, TimeInterval PermissionWindow);

/// <summary>Everything needed to compute one attendance day. No database access, no clock.</summary>
public sealed record AttendanceCalculationInput(
    TimeInterval ScheduledWindow,
    int BreakMinutes,
    int GraceMinutes,
    bool CountEarlyArrivalAsOvertime,
    int MinimumOvertimeMinutes,
    DateTimeOffset? CheckInAt,
    DateTimeOffset? CheckOutAt,
    CheckOutType? CheckOutType,
    IReadOnlyList<ExitSpan> Exits,
    IReadOnlyList<TimeInterval> ApprovedLateWindows,
    IReadOnlyList<TimeInterval> ApprovedEarlyDepartureWindows);

/// <summary>All values in whole minutes. See spec section 3.8.</summary>
public sealed record AttendanceCalculationResult(
    int GrossMinutes,
    int PermissionMinutes,
    int NetWorkMinutes,
    int LateTotal,
    int LateExcused,
    int LateReturnMinutes,
    int LateUnexcused,
    int EarlyTotal,
    int EarlyExcused,
    int EarlyUnexcused,
    int OvertimeMinutes)
{
    public static readonly AttendanceCalculationResult Empty = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
