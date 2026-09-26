using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Api.Features.Attendance;
using FieldAttendance.Api.Features.Schedule;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Hr;

/// <summary>
/// Keeps an office employee's assignments in step with their fixed weekly schedule.
///
/// Office attendance deliberately reuses the field machinery: a weekly pattern becomes
/// assignments (branch + shift template + working days), which the resolver, the
/// materializer and the calculator already understand. One engine, one set of numbers.
/// </summary>
public sealed class OfficeScheduleService(
    AppDbContext db, ScheduleService schedule, MaterializationService materializer, IClock clock)
{
    /// <summary>
    /// Applies the employee's schedule from <paramref name="effectiveFrom"/>: closes the assignments
    /// that no longer match and creates the ones the pattern needs. Past days keep their own records.
    /// </summary>
    public async Task ApplyAsync(EmployeeProfile profile, DateOnly effectiveFrom, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (effectiveFrom < clock.Today) effectiveFrom = clock.Today;

        var current = await db.Assignments
            .Where(a => a.EmployeeId == profile.UserId && a.IsActive && (a.EndDate == null || a.EndDate >= effectiveFrom))
            .ToListAsync(ct);

        var wanted = profile.WorkScheduleId is { } scheduleId && profile.IsEmployed
            ? await PlanAsync(scheduleId, profile.BranchLocationId, ct)
            : [];

        // Anything that is not part of the new pattern stops at the effective date.
        foreach (var assignment in current)
        {
            var keep = wanted.Any(w => w.LocationId == assignment.LocationId
                                       && w.ShiftTemplateId == assignment.ShiftTemplateId
                                       && w.Days == assignment.Days);
            if (!keep) assignment.EndBefore(effectiveFrom);
        }

        foreach (var want in wanted)
        {
            var exists = current.Any(a => a.IsActive && a.LocationId == want.LocationId
                                          && a.ShiftTemplateId == want.ShiftTemplateId && a.Days == want.Days
                                          && (a.EndDate == null || a.EndDate >= effectiveFrom));
            if (!exists)
                db.Assignments.Add(new Assignment(profile.UserId, want.LocationId, want.ShiftTemplateId,
                    effectiveFrom, null, want.Days, null));
        }

        // Validation (no overlapping shifts, branch capacity) and re-materialisation happen here.
        await schedule.SaveAsync([profile.UserId], effectiveFrom, null, ct);
    }

    /// <summary>Ends all assignments from a date: used when service ends or the employee is suspended.</summary>
    public async Task StopAsync(Guid employeeId, DateOnly effectiveFrom, CancellationToken ct)
    {
        if (effectiveFrom < clock.Today) effectiveFrom = clock.Today;
        var current = await db.Assignments
            .Where(a => a.EmployeeId == employeeId && a.IsActive && (a.EndDate == null || a.EndDate >= effectiveFrom))
            .ToListAsync(ct);
        foreach (var assignment in current) assignment.EndBefore(effectiveFrom);

        db.AttendanceRecords.RemoveRange(await db.AttendanceRecords
            .Where(r => r.EmployeeId == employeeId && r.ShiftDate >= effectiveFrom && r.CheckInAt == null).ToListAsync(ct));

        await db.SaveChangesAsync(ct);
        await materializer.SyncAsync([employeeId], clock.Today, clock.Today.AddDays(1), ct);
    }

    private async Task<List<(Guid LocationId, Guid ShiftTemplateId, Domain.Enums.WorkDays Days)>> PlanAsync(
        Guid scheduleId, Guid branchLocationId, CancellationToken ct)
    {
        var workSchedule = await db.WorkSchedules.Include(s => s.Days).SingleOrDefaultAsync(s => s.Id == scheduleId && s.IsActive, ct)
            ?? throw new DomainException("schedule.not_found", "Work schedule not found.");
        if (!await db.Locations.AnyAsync(l => l.Id == branchLocationId && l.IsActive, ct))
            throw new DomainException("branch.not_found", "Branch not found.");

        var plan = new List<(Guid, Guid, Domain.Enums.WorkDays)>();
        foreach (var group in OfficeSchedulePlanner.Plan(workSchedule))
        {
            var shiftId = await schedule.EnsureShiftTemplateAsync(group.Start, group.End, ct,
                group.BreakMinutes, workSchedule.GraceMinutes, workSchedule.EarlyCheckInMinutes,
                workSchedule.CountEarlyArrivalAsOvertime);
            plan.Add((branchLocationId, shiftId, group.Days));
        }
        return plan;
    }
}
