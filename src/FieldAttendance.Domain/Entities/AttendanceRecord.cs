using FieldAttendance.Domain.Attendance;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Geo;
using FieldAttendance.Domain.Scheduling;
using FieldAttendance.Domain.Time;

namespace FieldAttendance.Domain.Entities;

/// <summary>
/// One row per employee per shift, materialized from <see cref="ResolvedShift"/>.
/// Keeps its own snapshot of the shift rules so later template edits never rewrite history.
/// Unique index: (EmployeeId, ScheduledStart).
/// </summary>
public sealed class AttendanceRecord : Entity
{
    private readonly List<TemporaryExit> _exits = [];

    private AttendanceRecord() { } // EF Core

    public AttendanceRecord(ResolvedShift shift)
    {
        ArgumentNullException.ThrowIfNull(shift);
        EmployeeId = shift.EmployeeId;
        ApplySchedule(shift);
    }

    public Guid EmployeeId { get; private set; }
    public DateOnly ShiftDate { get; private set; }
    public Guid LocationId { get; private set; }
    public Guid ShiftTemplateId { get; private set; }
    public DateTimeOffset ScheduledStart { get; private set; }
    public DateTimeOffset ScheduledEnd { get; private set; }
    public int BreakMinutes { get; private set; }
    public int GraceMinutes { get; private set; }
    public bool CountEarlyArrivalAsOvertime { get; private set; }
    public int EarlyCheckInMinutes { get; private set; }

    public DateTimeOffset? CheckInAt { get; private set; }
    public double? CheckInLat { get; private set; }
    public double? CheckInLng { get; private set; }
    public double? CheckInAccuracy { get; private set; }
    public double? CheckInDistance { get; private set; }
    public CheckInType? CheckInType { get; private set; }

    public DateTimeOffset? CheckOutAt { get; private set; }
    public double? CheckOutLat { get; private set; }
    public double? CheckOutLng { get; private set; }
    public CheckOutType? CheckOutType { get; private set; }

    public AttendanceStatus Status { get; private set; } = AttendanceStatus.Scheduled;

    public int GrossMinutes { get; private set; }
    public int PermissionMinutes { get; private set; }
    public int NetWorkMinutes { get; private set; }
    public int LateTotal { get; private set; }
    public int LateExcused { get; private set; }
    public int LateUnexcused { get; private set; }
    public int EarlyTotal { get; private set; }
    public int EarlyExcused { get; private set; }
    public int EarlyUnexcused { get; private set; }
    public int OvertimeMinutes { get; private set; }

    public IReadOnlyCollection<TemporaryExit> Exits => _exits.AsReadOnly();

    public TimeInterval ScheduledWindow => new(ScheduledStart, ScheduledEnd);
    public TemporaryExit? OpenExit => _exits.FirstOrDefault(e => e.ReturnAt is null);
    public bool HasCheckedIn => CheckInAt is not null;
    public bool IsOpen => HasCheckedIn && CheckOutAt is null;

    /// <summary>Schedule sync. Only allowed before the employee checks in; history is never rewritten.</summary>
    public void ApplySchedule(ResolvedShift shift)
    {
        ArgumentNullException.ThrowIfNull(shift);
        if (HasCheckedIn)
            throw new DomainException("attendance.locked_after_checkin", "A record with a check-in cannot be rescheduled.");
        if (shift.EmployeeId != EmployeeId)
            throw new DomainException("attendance.employee_mismatch", "Shift belongs to another employee.");

        ShiftDate = shift.ShiftDate;
        LocationId = shift.LocationId;
        ShiftTemplateId = shift.ShiftTemplateId;
        ScheduledStart = shift.Window.Start;
        ScheduledEnd = shift.Window.End;
        BreakMinutes = shift.BreakMinutes;
        GraceMinutes = shift.GraceMinutes;
        CountEarlyArrivalAsOvertime = shift.CountEarlyArrivalAsOvertime;
        EarlyCheckInMinutes = shift.EarlyCheckInMinutes;
        Status = shift.IsOnLeave ? AttendanceStatus.OnLeave : AttendanceStatus.Scheduled;
    }

    public void CheckIn(DateTimeOffset at, GeoPoint point, double accuracy, double distance, CheckInType type)
    {
        if (Status == AttendanceStatus.OnLeave)
            throw new DomainException("attendance.on_leave", "Employee is on approved leave for this shift.");
        if (HasCheckedIn)
            throw new DomainException("attendance.already_checked_in", "Already checked in for this shift.");
        if (at >= ScheduledEnd)
            throw new DomainException("attendance.shift_ended", "This shift has already ended.");
        if (type == Enums.CheckInType.Normal && at < ScheduledStart.AddMinutes(-EarlyCheckInMinutes))
            throw new DomainException("attendance.too_early", "Check-in opens shortly before the shift starts.");

        CheckInAt = at;
        CheckInLat = point.Latitude;
        CheckInLng = point.Longitude;
        CheckInAccuracy = accuracy;
        CheckInDistance = distance;
        CheckInType = type;
        Status = AttendanceStatus.Present; // also clears a provisional Absent after an approved exception
    }

    public TemporaryExit StartTemporaryExit(DateTimeOffset at, Guid permissionRequestId, TimeInterval permissionWindow)
    {
        EnsureOpen();
        if (OpenExit is not null)
            throw new DomainException("attendance.exit_already_open", "A temporary exit is already in progress.");
        if (!permissionWindow.Contains(UaeTime.TruncateToMinute(at)))
            throw new DomainException("attendance.exit_outside_permission", "Temporary exit is only allowed within the approved permission time.");
        if (_exits.Any(e => e.PermissionRequestId == permissionRequestId))
            throw new DomainException("attendance.permission_used", "This permission has already been used.");

        var exit = new TemporaryExit(Id, permissionRequestId, at, permissionWindow);
        _exits.Add(exit);
        return exit;
    }

    public void ReturnFromExit(DateTimeOffset at, GeoPoint point)
    {
        var exit = OpenExit ?? throw new DomainException("attendance.no_open_exit", "There is no temporary exit to return from.");
        exit.Return(at, point);
    }

    public void CheckOut(DateTimeOffset at, GeoPoint point, CheckOutType type)
    {
        if (type is Enums.CheckOutType.Auto or Enums.CheckOutType.UnreturnedExit)
            throw new DomainException("attendance.invalid_checkout_type", "System check-out types are set by AutoClose only.");
        EnsureOpen();
        if (OpenExit is not null)
            throw new DomainException("attendance.return_first", "Register your return before checking out.");
        if (at < CheckInAt)
            throw new DomainException("attendance.checkout_before_checkin", "Check-out cannot be before check-in.");

        CheckOutAt = at;
        CheckOutLat = point.Latitude;
        CheckOutLng = point.Longitude;
        CheckOutType = type;
        Status = AttendanceStatus.CheckedOut;
    }

    /// <summary>
    /// Closes a forgotten check-out once the shift end + delay has passed.
    /// Open temporary exit → closed at exit time (UnreturnedExit); otherwise at shift end (Auto).
    /// Idempotent: returns false when there is nothing to do.
    /// </summary>
    public bool AutoClose(DateTimeOffset now, int delayMinutes)
    {
        if (!IsOpen || now < ScheduledEnd.AddMinutes(delayMinutes))
            return false;

        if (OpenExit is { } exit)
        {
            CheckOutAt = exit.ExitAt;
            CheckOutType = Enums.CheckOutType.UnreturnedExit;
        }
        else
        {
            CheckOutAt = ScheduledEnd;
            CheckOutType = Enums.CheckOutType.Auto;
        }
        Status = AttendanceStatus.CheckedOut;
        return true;
    }

    /// <summary>Idempotent absence marking at shift end (spec 3.7).</summary>
    public bool MarkAbsentIfDue(DateTimeOffset now, bool hasPendingException)
    {
        if (Status != AttendanceStatus.Scheduled || HasCheckedIn || hasPendingException || now < ScheduledEnd)
            return false;
        Status = AttendanceStatus.Absent;
        return true;
    }

    public void MarkOnLeave()
    {
        if (HasCheckedIn)
            throw new DomainException("attendance.leave_after_checkin", "Cannot apply leave to a shift already attended.");
        Status = AttendanceStatus.OnLeave;
    }

    public void ClearLeave(DateTimeOffset now)
    {
        if (Status != AttendanceStatus.OnLeave) return;
        Status = AttendanceStatus.Scheduled;
        MarkAbsentIfDue(now, hasPendingException: false);
    }

    public AttendanceCalculationInput BuildCalculationInput(
        IEnumerable<TimeInterval> approvedLateWindows,
        IEnumerable<TimeInterval> approvedEarlyDepartureWindows,
        int minimumOvertimeMinutes) =>
        new(ScheduledWindow, BreakMinutes, GraceMinutes, CountEarlyArrivalAsOvertime, minimumOvertimeMinutes,
            CheckInAt, CheckOutAt, CheckOutType,
            _exits.Select(e => new ExitSpan(e.ExitAt, e.ReturnAt, e.PermissionWindow)).ToList(),
            approvedLateWindows.ToList(), approvedEarlyDepartureWindows.ToList());

    public void ApplyCalculation(AttendanceCalculationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        GrossMinutes = result.GrossMinutes;
        PermissionMinutes = result.PermissionMinutes;
        NetWorkMinutes = result.NetWorkMinutes;
        LateTotal = result.LateTotal;
        LateExcused = result.LateExcused;
        LateUnexcused = result.LateUnexcused;
        EarlyTotal = result.EarlyTotal;
        EarlyExcused = result.EarlyExcused;
        EarlyUnexcused = result.EarlyUnexcused;
        OvertimeMinutes = result.OvertimeMinutes;
    }

    private void EnsureOpen()
    {
        if (!IsOpen)
            throw new DomainException("attendance.not_present", "Employee is not currently checked in.");
    }
}

public sealed class TemporaryExit : Entity
{
    private TemporaryExit() { } // EF Core

    internal TemporaryExit(Guid attendanceRecordId, Guid permissionRequestId, DateTimeOffset exitAt, TimeInterval permissionWindow)
    {
        AttendanceRecordId = attendanceRecordId;
        PermissionRequestId = Guard.NotEmpty(permissionRequestId, "exit.permission");
        ExitAt = exitAt;
        PermissionStart = permissionWindow.Start;
        PermissionEnd = permissionWindow.End;
    }

    public Guid AttendanceRecordId { get; private set; }
    public Guid PermissionRequestId { get; private set; }
    public DateTimeOffset ExitAt { get; private set; }
    public DateTimeOffset? ReturnAt { get; private set; }
    public double? ReturnLat { get; private set; }
    public double? ReturnLng { get; private set; }

    /// <summary>Snapshot of the approved window at exit time.</summary>
    public DateTimeOffset PermissionStart { get; private set; }
    public DateTimeOffset PermissionEnd { get; private set; }

    public TimeInterval PermissionWindow => new(PermissionStart, PermissionEnd);

    internal void Return(DateTimeOffset at, GeoPoint point)
    {
        if (at < ExitAt)
            throw new DomainException("exit.return_before_exit", "Return cannot be before exit.");
        ReturnAt = at;
        ReturnLat = point.Latitude;
        ReturnLng = point.Longitude;
    }
}
