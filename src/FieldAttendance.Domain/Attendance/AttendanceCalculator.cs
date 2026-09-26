using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Time;

namespace FieldAttendance.Domain.Attendance;

/// <summary>
/// The only place attendance minutes are computed (spec 3.8). Pure and deterministic:
/// the dashboard, the attendance page and every report read the values it produces.
/// All instants are truncated to the minute before use.
/// </summary>
public static class AttendanceCalculator
{
    public static AttendanceCalculationResult Calculate(AttendanceCalculationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.CheckInAt is not { } rawCheckIn)
            return AttendanceCalculationResult.Empty;

        var shift = input.ScheduledWindow;
        var checkIn = UaeTime.TruncateToMinute(rawCheckIn);
        var (lateTotal, lateExcused) = Late(shift, checkIn, input.GraceMinutes, input.ApprovedLateWindows);

        // Still on shift: only lateness is final. Hours are computed at check-out.
        if (input.CheckOutAt is not { } rawCheckOut)
        {
            return AttendanceCalculationResult.Empty with
            {
                LateTotal = lateTotal,
                LateExcused = lateExcused,
                LateUnexcused = lateTotal - lateExcused,
            };
        }

        var checkOut = UaeTime.TruncateToMinute(rawCheckOut);
        if (checkOut < checkIn)
            throw new DomainException("attendance.checkout_before_checkin", "Check-out cannot be before check-in.");

        // Arriving early only counts as work when the shift says so; otherwise the day
        // starts at the scheduled time, so an early check-in neither pays overtime nor
        // inflates net hours (spec 3.8).
        var workStart = input.CountEarlyArrivalAsOvertime || checkIn > shift.Start ? checkIn : shift.Start;
        var presence = new TimeInterval(workStart, checkOut > workStart ? checkOut : workStart);
        var (permissionMinutes, lateReturn) = Exits(presence, input.Exits);
        var (earlyTotal, earlyExcused) = Early(shift, checkOut, input.CheckOutType, input.ApprovedEarlyDepartureWindows, input.Exits);
        var overtime = Overtime(shift, checkIn, checkOut, input.CheckOutType, input.CountEarlyArrivalAsOvertime, input.MinimumOvertimeMinutes);
        var net = Math.Max(0, presence.Minutes - permissionMinutes - input.BreakMinutes);

        return new AttendanceCalculationResult(
            GrossMinutes: presence.Minutes,
            PermissionMinutes: permissionMinutes,
            NetWorkMinutes: net,
            LateTotal: lateTotal,
            LateExcused: lateExcused,
            LateReturnMinutes: lateReturn,
            LateUnexcused: lateTotal - lateExcused + lateReturn,
            EarlyTotal: earlyTotal,
            EarlyExcused: earlyExcused,
            EarlyUnexcused: earlyTotal - earlyExcused,
            OvertimeMinutes: overtime);
    }

    /// <summary>Grace is tolerance, not a deduction: past it, lateness counts from shift start.</summary>
    private static (int Total, int Excused) Late(TimeInterval shift, DateTimeOffset checkIn, int graceMinutes, IEnumerable<TimeInterval> lateWindows)
    {
        if (checkIn <= shift.Start.AddMinutes(graceMinutes))
            return (0, 0);

        var lateSpan = new TimeInterval(shift.Start, checkIn);
        return (lateSpan.Minutes, lateSpan.CoveredMinutes(lateWindows));
    }

    private static (int PermissionMinutes, int LateReturn) Exits(TimeInterval presence, IEnumerable<ExitSpan> exits)
    {
        var used = new List<TimeInterval>();
        var lateReturn = 0;

        foreach (var exit in exits)
        {
            var exitAt = UaeTime.TruncateToMinute(exit.ExitAt);
            var backAt = exit.ReturnAt is { } r ? UaeTime.TruncateToMinute(r) : presence.End;
            if (backAt > exitAt && new TimeInterval(exitAt, backAt).Intersect(presence) is { } away)
                used.Add(away);

            if (exit.ReturnAt is { } returned)
            {
                var overrun = (int)(UaeTime.TruncateToMinute(returned) - exit.PermissionWindow.End).TotalMinutes;
                lateReturn += Math.Max(0, overrun);
            }
        }

        var permissionMinutes = TimeInterval.Merge(used).Sum(i => i.Minutes);
        return (permissionMinutes, lateReturn);
    }

    /// <summary>
    /// Excused if covered by an approved early-departure window, or by the unused part of a
    /// temporary-exit window the employee never returned from (spec 3.5).
    /// </summary>
    private static (int Total, int Excused) Early(TimeInterval shift, DateTimeOffset checkOut, CheckOutType? type,
        IEnumerable<TimeInterval> earlyWindows, IEnumerable<ExitSpan> exits)
    {
        if (type == CheckOutType.Auto || checkOut >= shift.End)
            return (0, 0);

        var missing = new TimeInterval(checkOut, shift.End);
        var excusing = earlyWindows.Concat(
            exits.Where(e => e.ReturnAt is null && e.PermissionWindow.End > UaeTime.TruncateToMinute(e.ExitAt))
                 .Select(e => new TimeInterval(e.ExitAt, e.PermissionWindow.End)));

        return (missing.Minutes, missing.CoveredMinutes(excusing));
    }

    private static int Overtime(TimeInterval shift, DateTimeOffset checkIn, DateTimeOffset checkOut, CheckOutType? type,
        bool countEarlyArrival, int minimumMinutes)
    {
        if (type is CheckOutType.Auto or CheckOutType.UnreturnedExit)
            return 0;

        var after = checkOut > shift.End ? (int)(checkOut - shift.End).TotalMinutes : 0;
        var before = countEarlyArrival && checkIn < shift.Start ? (int)(shift.Start - checkIn).TotalMinutes : 0;
        var total = after + before;
        return total >= Math.Max(0, minimumMinutes) ? total : 0;
    }
}
