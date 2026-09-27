using System.ComponentModel.DataAnnotations;
using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Approvals;

public sealed record FlowDto(Guid Id, string Kind, string NameAr, string NameEn, int EscalationHours, IReadOnlyList<string> Stages,
    byte[] RowVersion, string? Workforce);

/// <param name="Workforce">Field or Office for that workforce's own chain; empty for the general one.</param>
public sealed record SaveFlowRequest([Required] string Kind, [Required] string NameAr, [Required] string NameEn,
    [Range(0, 720)] int EscalationHours, IReadOnlyList<string> Stages, byte[]? RowVersion, string? Workforce = null);

public sealed record ReturnDto(Guid Id, Guid EmployeeId, string EmployeeName, Guid LeaveRequestId, DateOnly ExpectedDate,
    DateOnly? ActualDate, string Status, int LateDays, string? Note, string? ConfirmedByName, bool IsOverdue);

public sealed record ReportReturnRequest(DateOnly ActualDate, string? Note);

/// <summary>Approval chains, and the return-to-work step that closes every leave.</summary>
[ApiController]
[Route("api/hr/approvals")]
[Authorize(Policy = HrPolicies.Read)]
public sealed class ApprovalsController(AppDbContext db, ICurrentUser me, IClock clock) : ControllerBase
{
    [HttpGet("flows")]
    public async Task<IReadOnlyList<FlowDto>> Flows(CancellationToken ct) =>
        (await db.ApprovalFlows.AsNoTracking().Include(f => f.Levels).Where(f => f.IsActive).ToListAsync(ct))
        .Select(f => new FlowDto(f.Id, f.Kind.ToString(), f.NameAr, f.NameEn, f.EscalationHours,
            f.Stages().Select(s => s.ToString()).ToList(), f.RowVersion, f.Workforce?.ToString())).ToList();

    /// <summary>One chain per request kind and workforce: saving replaces the existing one for that pair.</summary>
    [HttpPut("flows")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> SaveFlow(SaveFlowRequest r, CancellationToken ct)
    {
        var kind = Enum.TryParse<RequestKind>(r.Kind, true, out var parsed) ? parsed
            : throw new DomainException("flow.kind", "Unknown request kind.");
        var stages = (r.Stages ?? []).Select(s => Enum.TryParse<ApprovalStage>(s, true, out var stage) ? stage
            : throw new DomainException("flow.stage", "Unknown approval level.")).ToList();

        Workforce? workforce = Enum.TryParse<Workforce>(r.Workforce, true, out var w) ? w : null;
        var flow = await db.ApprovalFlows.Include(f => f.Levels)
            .FirstOrDefaultAsync(f => f.Kind == kind && f.Workforce == workforce && f.IsActive, ct);
        if (flow is null)
        {
            flow = new ApprovalFlow(kind, r.NameAr, r.NameEn, r.EscalationHours, workforce);
            db.ApprovalFlows.Add(flow);
        }
        else
        {
            db.ExpectVersion(flow, r.RowVersion);
            flow.Rename(r.NameAr, r.NameEn);
            flow.SetEscalation(r.EscalationHours);
        }

        flow.SetLevels(stages);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("returns")]
    public async Task<IReadOnlyList<ReturnDto>> Returns([FromQuery] bool onlyOpen = true, CancellationToken ct = default)
    {
        var query = db.ReturnsToWork.AsNoTracking().Where(r => r.IsActive);
        if (onlyOpen) query = query.Where(r => r.Status != ReturnStatus.Confirmed);

        var list = await query.OrderBy(r => r.ExpectedDate).Take(500).ToListAsync(ct);
        var names = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var today = clock.Today;

        return list.Select(r => new ReturnDto(r.Id, r.EmployeeId, names.GetValueOrDefault(r.EmployeeId, "?"),
            r.LeaveRequestId, r.ExpectedDate, r.ActualDate, r.Status.ToString(), r.LateDays, r.Note,
            r.ConfirmedBy is { } c ? names.GetValueOrDefault(c) : null, r.IsOverdue(today))).ToList();
    }

    [HttpPost("returns/{id:guid}/confirm")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken ct)
    {
        var record = await db.ReturnsToWork.SingleOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new DomainException("return.not_found", "Return record not found.");
        record.Confirm(me.RequiredId, clock.Now);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

/// <summary>The employee's own side: report back from leave and follow a request's chain.</summary>
[ApiController]
[Route("api/my/returns")]
[Authorize(Policy = HrPolicies.Self)]
public sealed class MyReturnsController(AppDbContext db, ICurrentUser me) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<ReturnDto>> Mine(CancellationToken ct)
    {
        var list = await db.ReturnsToWork.AsNoTracking()
            .Where(r => r.EmployeeId == me.RequiredId && r.IsActive)
            .OrderByDescending(r => r.ExpectedDate).Take(50).ToListAsync(ct);
        return list.Select(r => new ReturnDto(r.Id, r.EmployeeId, "", r.LeaveRequestId, r.ExpectedDate, r.ActualDate,
            r.Status.ToString(), r.LateDays, r.Note, null, false)).ToList();
    }

    [HttpPost("{id:guid}/report")]
    public async Task<IActionResult> Report(Guid id, ReportReturnRequest r, CancellationToken ct)
    {
        var record = await db.ReturnsToWork.SingleOrDefaultAsync(x => x.Id == id && x.EmployeeId == me.RequiredId, ct)
            ?? throw new DomainException("return.not_found", "Return record not found.");
        record.Report(r.ActualDate, r.Note);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
