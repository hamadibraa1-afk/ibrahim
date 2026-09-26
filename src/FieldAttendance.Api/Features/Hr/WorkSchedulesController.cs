using System.ComponentModel.DataAnnotations;
using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Hr;

public sealed record ScheduleDayDto(DayOfWeek Day, TimeOnly StartTime, TimeOnly EndTime, int BreakMinutes);

public sealed record WorkScheduleDto(Guid Id, string NameAr, string NameEn, int GraceMinutes, int EarlyCheckInMinutes,
    bool CountEarlyArrivalAsOvertime, int WeeklyMinutes, int EmployeeCount, IReadOnlyList<ScheduleDayDto> Days,
    bool IsActive, byte[] RowVersion, int FlexMinutes);

public sealed record SaveWorkScheduleRequest([Required] string NameAr, [Required] string NameEn,
    [Range(0, 120)] int GraceMinutes, [Range(0, 240)] int EarlyCheckInMinutes, bool CountEarlyArrivalAsOvertime,
    IReadOnlyList<ScheduleDayDto> Days, byte[]? RowVersion, [Range(0, 120)] int FlexMinutes = 0);

/// <summary>Fixed weekly patterns for office staff. Changing one re-applies it to everyone on it.</summary>
[ApiController]
[Route("api/hr/work-schedules")]
[Authorize(Policy = HrPolicies.Read)]
public sealed class WorkSchedulesController(AppDbContext db, OfficeScheduleService office, IClock clock) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<WorkScheduleDto>> List([FromQuery] bool includeInactive, CancellationToken ct)
    {
        var schedules = await db.WorkSchedules.AsNoTracking().Include(s => s.Days)
            .Where(s => includeInactive || s.IsActive).OrderBy(s => s.NameAr).ToListAsync(ct);
        var counts = await db.EmployeeProfiles.AsNoTracking().Where(p => p.IsActive && p.WorkScheduleId != null)
            .GroupBy(p => p.WorkScheduleId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        return schedules.Select(s => ToDto(s, counts.FirstOrDefault(c => c.Key == s.Id)?.Count ?? 0)).ToList();
    }

    [HttpPost]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<ActionResult<WorkScheduleDto>> Create(SaveWorkScheduleRequest r, CancellationToken ct)
    {
        var schedule = new WorkSchedule(r.NameAr, r.NameEn, r.GraceMinutes, r.EarlyCheckInMinutes, r.CountEarlyArrivalAsOvertime, r.FlexMinutes);
        ApplyDays(schedule, r.Days);
        db.WorkSchedules.Add(schedule);
        await db.SaveChangesAsync(ct);
        return ToDto(schedule, 0);
    }

    /// <summary>Applies to upcoming days for everyone on this schedule; past records keep their own times.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<ActionResult<WorkScheduleDto>> Update(Guid id, SaveWorkScheduleRequest r, CancellationToken ct)
    {
        var schedule = await db.WorkSchedules.Include(s => s.Days).SingleOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new DomainException("schedule.not_found", "Work schedule not found.");

        db.ExpectVersion(schedule, r.RowVersion);
        schedule.Rename(r.NameAr, r.NameEn);
        schedule.SetRules(r.GraceMinutes, r.EarlyCheckInMinutes, r.CountEarlyArrivalAsOvertime, r.FlexMinutes);
        ApplyDays(schedule, r.Days);
        await db.SaveChangesAsync(ct);

        foreach (var profile in await db.EmployeeProfiles.Where(p => p.WorkScheduleId == id && p.IsActive).ToListAsync(ct))
            await office.ApplyAsync(profile, clock.Today, ct);

        var count = await db.EmployeeProfiles.CountAsync(p => p.WorkScheduleId == id && p.IsActive, ct);
        return ToDto(schedule, count);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var schedule = await db.WorkSchedules.SingleOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new DomainException("schedule.not_found", "Work schedule not found.");
        if (await db.EmployeeProfiles.AnyAsync(p => p.WorkScheduleId == id && p.IsActive, ct))
            throw new DomainException("schedule.in_use", "Move its employees to another schedule first.");
        schedule.Deactivate();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static void ApplyDays(WorkSchedule schedule, IReadOnlyList<ScheduleDayDto> days)
    {
        if (days is not { Count: > 0 })
            throw new DomainException("schedule.no_days", "Add at least one working day.");

        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var wanted = days.SingleOrDefault(d => d.Day == day);
            if (wanted is null) schedule.RemoveDay(day);
            else schedule.SetDay(day, wanted.StartTime, wanted.EndTime, wanted.BreakMinutes);
        }
        schedule.EnsureUsable();
    }

    private static WorkScheduleDto ToDto(WorkSchedule s, int employeeCount) =>
        new(s.Id, s.NameAr, s.NameEn, s.GraceMinutes, s.EarlyCheckInMinutes, s.CountEarlyArrivalAsOvertime,
            s.WeeklyMinutes, employeeCount,
            s.Days.OrderBy(d => d.Day).Select(d => new ScheduleDayDto(d.Day, d.StartTime, d.EndTime, d.BreakMinutes)).ToList(),
            s.IsActive, s.RowVersion, s.FlexMinutes);
}
