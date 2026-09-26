using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Api.Features.Attendance;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Schedule;

/// <summary>
/// Every schedule change goes through here: validate against spec 3.2 hard constraints,
/// save, then re-sync materialized attendance records in the same request.
/// </summary>
public sealed class ScheduleService(AppDbContext db, ScheduleSnapshotLoader loader, MaterializationService materializer, IClock clock)
{
    /// <summary>Open-ended assignments are validated this far ahead.</summary>
    public const int ValidationHorizonDays = 62;

    /// <summary>Grace period given to shift templates created from the location screen.</summary>
    public const int DefaultGraceMinutes = 10;

    public async Task SaveAsync(IReadOnlyCollection<Guid> affectedEmployees, DateOnly from, DateOnly? to, CancellationToken ct)
    {
        var validateTo = to is { } end && end < from.AddDays(ValidationHorizonDays) ? end : from.AddDays(ValidationHorizonDays);
        await ValidateAsync(affectedEmployees, from.AddDays(-1), validateTo, ct);
        await db.SaveChangesAsync(ct);
        await materializer.SyncAsync(affectedEmployees, clock.Today, clock.Today.AddDays(1), ct);
    }

    private async Task ValidateAsync(IReadOnlyCollection<Guid> employees, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var snapshot = await loader.LoadAsync(null, from, to, ct);
        snapshot = WithPendingChanges(snapshot);

        var allCollectors = await loader.ActiveCollectorIdsAsync(ct);
        var everyone = allCollectors.Union(employees).ToList();
        var shifts = everyone.SelectMany(e => ScheduleResolver.ResolveRange(e, from, to, snapshot)).ToList();

        var overlaps = ScheduleValidator.FindEmployeeOverlaps(shifts.Where(s => employees.Contains(s.EmployeeId)));
        if (overlaps.Count > 0)
            throw new DomainException("schedule.overlap",
                $"Overlapping shifts for the same employee on {overlaps[0].First.ShiftDate:yyyy-MM-dd}.");

        var affectedLocations = shifts.Where(s => employees.Contains(s.EmployeeId)).Select(s => s.LocationId).Distinct().ToList();
        var locations = await db.Locations.AsNoTracking().Where(l => affectedLocations.Contains(l.Id)).ToListAsync(ct);
        foreach (var location in locations)
        {
            var violations = ScheduleValidator.FindCapacityViolations(location.Id, location.Capacity, shifts);
            if (violations.Count > 0 && violations[0] is var violation)
                throw new DomainException("schedule.capacity",
                    $"{location.NameAr}: {violation.Assigned} employees exceed capacity {violation.Capacity} on {violation.During.Start:yyyy-MM-dd HH:mm}.");
        }
    }

    /// <summary>Overlay unsaved tracked changes so validation sees the schedule as it will be after save.</summary>
    private ScheduleSnapshot WithPendingChanges(ScheduleSnapshot s)
    {
        var assignments = s.Assignments.ToDictionary(a => a.Id);
        foreach (var e in db.ChangeTracker.Entries<Assignment>().Where(e => e.State is EntityState.Added or EntityState.Modified))
            assignments[e.Entity.Id] = e.Entity;
        var overrides = s.Overrides.ToDictionary(o => o.Id);
        foreach (var e in db.ChangeTracker.Entries<AssignmentOverride>().Where(e => e.State is EntityState.Added or EntityState.Modified))
            overrides[e.Entity.Id] = e.Entity;

        // A rule change (grace, flexible hours) creates its template in the same save as the assignments
        // that use it; without it here, validation cannot resolve those assignments and the save fails.
        var templates = s.ShiftTemplates.ToDictionary(p => p.Key, p => p.Value);
        foreach (var e in db.ChangeTracker.Entries<ShiftTemplate>().Where(e => e.State == EntityState.Added))
            templates[e.Entity.Id] = e.Entity;

        // Inactive (ended/cancelled) entities are filtered by Covers(); keep them so modifications win over stale copies.
        return s with { Assignments = assignments.Values.ToList(), Overrides = overrides.Values.ToList(), ShiftTemplates = templates };
    }

    /// <summary>
    /// Assignments carry a shift template, but the location screen lets the supervisor type plain
    /// times. We reuse an existing template with the same times, or create one named after them,
    /// so ad-hoc assignments never duplicate templates.
    /// </summary>
    public async Task<Guid> EnsureShiftTemplateAsync(TimeOnly start, TimeOnly end, CancellationToken ct,
        int breakMinutes = 0, int? graceMinutes = null, int earlyCheckInMinutes = 0, bool countEarlyArrivalAsOvertime = false,
        int flexMinutes = 0)
    {
        if (start == end)
            throw new DomainException("shift.zero_length", "Start and end time cannot be equal.");

        var grace = graceMinutes ?? DefaultGraceMinutes;
        var existing = await db.ShiftTemplates.FirstOrDefaultAsync(s => s.IsActive
            && s.StartTime == start && s.EndTime == end && s.BreakMinutes == breakMinutes
            && s.GraceMinutes == grace && s.EarlyCheckInMinutes == earlyCheckInMinutes
            && s.CountEarlyArrivalAsOvertime == countEarlyArrivalAsOvertime && s.FlexMinutes == flexMinutes, ct);
        if (existing is not null) return existing.Id;

        var name = $"{start:HH\\:mm} - {end:HH\\:mm}";
        var template = new ShiftTemplate(name, name, start, end, breakMinutes, grace,
            countEarlyArrivalAsOvertime, earlyCheckInMinutes);
        template.SetFlex(flexMinutes);
        db.ShiftTemplates.Add(template);
        return template.Id;
    }

    public async Task<IReadOnlyList<Guid>> AvailableEmployeesAsync(DateOnly date, CancellationToken ct)
    {
        var employees = await loader.ActiveCollectorIdsAsync(ct);
        var snapshot = await loader.LoadAsync(employees, date, date, ct);
        var onLeave = snapshot.Leaves.Where(l => l.CoversApproved(date)).Select(l => l.EmployeeId).ToHashSet();
        return employees.Where(e => !onLeave.Contains(e) && ScheduleResolver.Resolve(e, date, snapshot).Count == 0).ToList();
    }

    public static void EnsureNotInPast(DateOnly date, IClock clock)
    {
        if (date < clock.Today)
            throw new DomainException("schedule.past_date", "Schedule changes cannot start in the past.");
    }

    public static OverrideType ParseOverrideType(string type) =>
        Enum.TryParse<OverrideType>(type, ignoreCase: true, out var t)
            ? t
            : throw new DomainException("schedule.invalid_type", "Unknown override type.");
}
