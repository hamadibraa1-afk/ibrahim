using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Time;

namespace FieldAttendance.Domain.Entities;

/// <summary>
/// Late (shift start → To), TemporaryExit (From → To), EarlyDeparture (From → shift end).
/// Times are wall-clock; <see cref="ToInterval"/> maps them onto the actual shift window,
/// including shifts that cross midnight.
/// </summary>
public sealed class PermissionRequest : ApprovableRequest
{
    private PermissionRequest() { } // EF Core

    private PermissionRequest(Guid employeeId, DateOnly shiftDate, PermissionType type, TimeOnly? fromTime, TimeOnly? toTime,
        string? reason, Guid submittedBy)
        : base(employeeId, reason, submittedBy, reasonRequired: true)
    {
        ShiftDate = shiftDate;
        Type = type;
        FromTime = fromTime;
        ToTime = toTime;
    }

    public DateOnly ShiftDate { get; private set; }
    public PermissionType Type { get; private set; }
    public TimeOnly? FromTime { get; private set; }
    public TimeOnly? ToTime { get; private set; }

    public static PermissionRequest Late(Guid employeeId, DateOnly shiftDate, TimeOnly until, TimeInterval shiftWindow, string reason, Guid submittedBy) =>
        Create(employeeId, shiftDate, PermissionType.Late, null, until, shiftWindow, reason, submittedBy);

    public static PermissionRequest TemporaryExit(Guid employeeId, DateOnly shiftDate, TimeOnly from, TimeOnly to, TimeInterval shiftWindow, string reason, Guid submittedBy) =>
        Create(employeeId, shiftDate, PermissionType.TemporaryExit, from, to, shiftWindow, reason, submittedBy);

    public static PermissionRequest EarlyDeparture(Guid employeeId, DateOnly shiftDate, TimeOnly from, TimeInterval shiftWindow, string reason, Guid submittedBy) =>
        Create(employeeId, shiftDate, PermissionType.EarlyDeparture, from, null, shiftWindow, reason, submittedBy);

    private static PermissionRequest Create(Guid employeeId, DateOnly shiftDate, PermissionType type, TimeOnly? from, TimeOnly? to,
        TimeInterval shiftWindow, string reason, Guid submittedBy)
    {
        var request = new PermissionRequest(employeeId, shiftDate, type, from, to, reason, submittedBy);
        _ = request.ToInterval(shiftWindow); // validates against the shift
        return request;
    }

    /// <summary>The concrete interval this permission covers inside <paramref name="shiftWindow"/>.</summary>
    public TimeInterval ToInterval(TimeInterval shiftWindow)
    {
        var start = Type == PermissionType.Late ? shiftWindow.Start : Map(shiftWindow, FromTime);
        var end = Type == PermissionType.EarlyDeparture ? shiftWindow.End : Map(shiftWindow, ToTime);
        if (end <= start)
            throw new DomainException("permission.empty_window", "Permission end must be after its start.");
        return new TimeInterval(start, end);
    }

    /// <summary>Employees may cancel pending requests, or approved ones whose window has not started.</summary>
    public void Cancel(DateTimeOffset now, TimeInterval shiftWindow)
    {
        var started = Status == RequestStatus.Approved && now >= ToInterval(shiftWindow).Start;
        if (started)
            throw new DomainException("permission.already_started", "An approved permission cannot be cancelled after it starts.");
        MarkCancelled(allowApproved: true);
    }

    private static DateTimeOffset Map(TimeInterval window, TimeOnly? time) =>
        time is { } t && ShiftTiming.MapIntoWindow(window, t) is { } mapped
            ? mapped
            : throw new DomainException("permission.outside_shift", "Permission times must fall within the shift.");
}
