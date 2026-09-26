using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;

namespace FieldAttendance.Domain.Scheduling;

/// <summary>Everything the resolver needs, preloaded by the caller for one or more employees.</summary>
public sealed record ScheduleSnapshot(
    IReadOnlyCollection<Assignment> Assignments,
    IReadOnlyCollection<AssignmentOverride> Overrides,
    IReadOnlyCollection<LeaveRequest> Leaves,
    IReadOnlyDictionary<Guid, ShiftTemplate> ShiftTemplates);

/// <summary>
/// Single source of truth for "what is this employee's schedule on date X".
/// Used by materialization, the scheduling grid, leave day counting and validation.
///
/// Rules:
/// 1. Overrides covering the date replace the base assignments for that whole day.
/// 2. Among overrides, later decisions win: the most recent Cancel voids every override
///    created before it; non-cancel overrides created after it are kept.
/// 3. Approved leave does not remove the shift; it flags it IsOnLeave so the location
///    shows as uncovered and the record is marked OnLeave.
/// </summary>
public static class ScheduleResolver
{
    public static IReadOnlyList<ResolvedShift> Resolve(Guid employeeId, DateOnly date, ScheduleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var isOnLeave = snapshot.Leaves.Any(l => l.EmployeeId == employeeId && l.CoversApproved(date));

        var overrides = snapshot.Overrides
            .Where(o => o.EmployeeId == employeeId && o.Covers(date))
            .OrderBy(o => o.CreatedAt)
            .ToList();

        var shifts = overrides.Count > 0
            ? ResolveOverrides(overrides, date, snapshot, isOnLeave)
            : ResolveBase(employeeId, date, snapshot, isOnLeave);

        return shifts.OrderBy(s => s.Window.Start).ToList();
    }

    public static IReadOnlyList<ResolvedShift> ResolveRange(Guid employeeId, DateOnly from, DateOnly to, ScheduleSnapshot snapshot)
    {
        if (to < from)
            throw new DomainException("schedule.range_invalid", "Range end cannot be before its start.");

        var result = new List<ResolvedShift>();
        for (var date = from; date <= to; date = date.AddDays(1))
            result.AddRange(Resolve(employeeId, date, snapshot));
        return result;
    }

    /// <summary>Leave is charged only for days the employee is actually scheduled to work.</summary>
    public static int CountScheduledWorkingDays(Guid employeeId, DateOnly from, DateOnly to, ScheduleSnapshot snapshot) =>
        ResolveRange(employeeId, from, to, snapshot).Select(s => s.ShiftDate).Distinct().Count();

    private static IEnumerable<ResolvedShift> ResolveOverrides(List<AssignmentOverride> ordered, DateOnly date, ScheduleSnapshot snapshot, bool isOnLeave)
    {
        var lastCancel = ordered.FindLastIndex(o => o.Type == OverrideType.Cancel);
        return ordered
            .Skip(lastCancel + 1)
            .Select(o => Build(o.EmployeeId, date, o.LocationId!.Value, o.ShiftTemplateId!.Value, snapshot,
                ShiftSource.Override, o.Id, o.Type, isOnLeave));
    }

    private static IEnumerable<ResolvedShift> ResolveBase(Guid employeeId, DateOnly date, ScheduleSnapshot snapshot, bool isOnLeave) =>
        snapshot.Assignments
            .Where(a => a.EmployeeId == employeeId && a.Covers(date))
            .Select(a => Build(employeeId, date, a.LocationId, a.ShiftTemplateId, snapshot,
                ShiftSource.BaseAssignment, a.Id, null, isOnLeave));

    private static ResolvedShift Build(Guid employeeId, DateOnly date, Guid locationId, Guid shiftTemplateId, ScheduleSnapshot snapshot,
        ShiftSource source, Guid sourceId, OverrideType? overrideType, bool isOnLeave)
    {
        if (!snapshot.ShiftTemplates.TryGetValue(shiftTemplateId, out var template))
            throw new DomainException("schedule.shift_missing", $"Shift template {shiftTemplateId} was not loaded.");

        return new ResolvedShift(employeeId, date, locationId, shiftTemplateId, template.WindowFor(date),
            template.BreakMinutes, template.GraceMinutes, template.CountEarlyArrivalAsOvertime, template.EarlyCheckInMinutes,
            source, sourceId, overrideType, isOnLeave);
    }
}
