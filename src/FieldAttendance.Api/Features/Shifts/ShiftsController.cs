using System.ComponentModel.DataAnnotations;
using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Shifts;

public sealed record ShiftDto(Guid Id, string NameAr, string NameEn, TimeOnly StartTime, TimeOnly EndTime, int DurationMinutes,
    int BreakMinutes, int GraceMinutes, bool CountEarlyArrivalAsOvertime, int EarlyCheckInMinutes, bool CrossesMidnight, bool IsActive, byte[] RowVersion);

public sealed record SaveShiftRequest(
    [Required] string NameAr, [Required] string NameEn, TimeOnly StartTime, TimeOnly EndTime,
    [Range(0, 600)] int BreakMinutes, [Range(0, 120)] int GraceMinutes, bool CountEarlyArrivalAsOvertime,
    [Range(0, 240)] int EarlyCheckInMinutes, byte[]? RowVersion);

[ApiController]
[Route("api/shifts")]
[Authorize(Policy = Policies.Read)]
public sealed class ShiftsController(AppDbContext db, IClock clock) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<ShiftDto>> List([FromQuery] bool includeInactive, CancellationToken ct) =>
        (await db.ShiftTemplates.AsNoTracking().Where(s => includeInactive || s.IsActive).OrderBy(s => s.StartTime).ToListAsync(ct))
        .Select(ToDto).ToList();

    [HttpPost]
    [Authorize(Policy = Policies.Manage)]
    public async Task<ActionResult<ShiftDto>> Create(SaveShiftRequest r, CancellationToken ct)
    {
        var shift = new ShiftTemplate(r.NameAr, r.NameEn, r.StartTime, r.EndTime, r.BreakMinutes, r.GraceMinutes,
            r.CountEarlyArrivalAsOvertime, r.EarlyCheckInMinutes);
        db.ShiftTemplates.Add(shift);
        await db.SaveChangesAsync(ct);
        return ToDto(shift);
    }

    /// <summary>Applies to records materialized from now on; existing records keep their snapshot (spec 5.5).</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<ActionResult<ShiftDto>> Update(Guid id, SaveShiftRequest r, CancellationToken ct)
    {
        var shift = await Find(id, ct);
        db.ExpectVersion(shift, r.RowVersion);
        shift.Rename(r.NameAr, r.NameEn);
        shift.SetTimes(r.StartTime, r.EndTime, r.BreakMinutes);
        shift.SetRules(r.GraceMinutes, r.CountEarlyArrivalAsOvertime, r.EarlyCheckInMinutes);
        await db.SaveChangesAsync(ct);
        return ToDto(shift);
    }

    /// <summary>Soft delete; attendance records keep their own snapshot of the shift.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<ActionResult<ShiftDto>> Delete(Guid id, CancellationToken ct)
    {
        var shift = await Find(id, ct);
        var today = clock.Today;
        if (await db.Assignments.AnyAsync(a => a.ShiftTemplateId == id && a.IsActive && (a.EndDate == null || a.EndDate >= today), ct))
            throw new DomainException("shift.in_use", "This shift is used by current or future assignments.");
        shift.Deactivate();
        await db.SaveChangesAsync(ct);
        return ToDto(shift);
    }

    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<ActionResult<ShiftDto>> Restore(Guid id, CancellationToken ct)
    {
        var shift = await Find(id, ct);
        shift.Activate();
        await db.SaveChangesAsync(ct);
        return ToDto(shift);
    }

    private async Task<ShiftTemplate> Find(Guid id, CancellationToken ct) =>
        await db.ShiftTemplates.SingleOrDefaultAsync(s => s.Id == id, ct)
        ?? throw new DomainException("shift.not_found", "Shift not found.");

    private static ShiftDto ToDto(ShiftTemplate s) =>
        new(s.Id, s.NameAr, s.NameEn, s.StartTime, s.EndTime, s.DurationMinutes, s.BreakMinutes, s.GraceMinutes,
            s.CountEarlyArrivalAsOvertime, s.EarlyCheckInMinutes, s.CrossesMidnight, s.IsActive, s.RowVersion);
}
