using System.ComponentModel.DataAnnotations;
using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Api.Features.Approvals;
using FieldAttendance.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Leaves;

public sealed record LeaveTypeDto(Guid Id, string NameAr, string NameEn, int? AnnualBalanceDays, bool IsActive);

public sealed record OnBehalfLeaveRequest(Guid EmployeeId, Guid LeaveTypeId, DateOnly FromDate, DateOnly ToDate, string? Reason);

public sealed record RejectLeaveRequest([Required] string Reason);

[ApiController]
[Route("api/leave-types")]
[Authorize]
public sealed class LeaveTypesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<LeaveTypeDto>> List(CancellationToken ct) =>
        await db.LeaveTypes.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.NameAr)
            .Select(t => new LeaveTypeDto(t.Id, t.NameAr, t.NameEn, t.AnnualBalanceDays, t.IsActive)).ToListAsync(ct);
}

/// <summary>Supervisor inbox for leave requests.</summary>
[ApiController]
[Route("api/requests/leaves")]
[Authorize(Policy = Policies.Read)]
public sealed class LeaveRequestsController(LeaveService leaves, ApprovalService approvals, ICurrentUser me) : ControllerBase
{
    /// <summary>Pending requests are filtered to the ones actually waiting on this approver.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<LeaveDto>> List([FromQuery] string status = "Pending", CancellationToken ct = default)
    {
        var parsed = Enum.TryParse<RequestStatus>(status, true, out var s) ? s : RequestStatus.Pending;
        var list = await leaves.ListAsync(null, parsed, ct);
        if (parsed != RequestStatus.Pending) return list;

        var isHr = User.IsInRole(nameof(UserRole.HrManager)) || User.IsInRole(nameof(UserRole.SystemAdmin));
        var mine = await approvals.WaitingOnAsync(RequestKind.Leave, me.RequiredId, isHr, ct);
        var waiting = mine.ToHashSet();
        return list.Where(l => waiting.Contains(l.Id)).ToList();
    }

    [HttpGet("{id:guid}/timeline")]
    public Task<IReadOnlyList<ApprovalStepView>> Timeline(Guid id, CancellationToken ct) => leaves.TimelineAsync(id, ct);

    [HttpPost]
    [Authorize(Policy = Policies.Manage)]
    public async Task<IActionResult> SubmitOnBehalf(OnBehalfLeaveRequest r, CancellationToken ct)
    {
        await leaves.SubmitAsync(r.EmployeeId, me.RequiredId, new SubmitLeaveRequest(r.LeaveTypeId, r.FromDate, r.ToDate, r.Reason), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        await leaves.DecideAsync(id, me.RequiredId, approve: true, null, ct,
            isHrOverride: User.IsInRole(nameof(UserRole.SystemAdmin)));
        return NoContent();
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<IActionResult> Reject(Guid id, RejectLeaveRequest r, CancellationToken ct)
    {
        await leaves.DecideAsync(id, me.RequiredId, approve: false, r.Reason, ct,
            isHrOverride: User.IsInRole(nameof(UserRole.SystemAdmin)));
        return NoContent();
    }

    /// <summary>Cancels an approved or pending leave: the balance returns and the shifts come back.</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await leaves.CancelAsync(id, null, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/me/leaves")]
[Authorize(Policy = Policies.Collector)]
public sealed class MyLeavesController(LeaveService leaves, ICurrentUser me) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<LeaveDto>> Mine(CancellationToken ct) => leaves.ListAsync(me.RequiredId, null, ct);

    /// <summary>Who has signed and who holds the request now.</summary>
    [HttpGet("{id:guid}/timeline")]
    public Task<IReadOnlyList<ApprovalStepView>> Timeline(Guid id, CancellationToken ct) => leaves.TimelineAsync(id, ct);

    [HttpGet("balances")]
    public Task<IReadOnlyList<LeaveBalanceDto>> Balances(CancellationToken ct) => leaves.BalancesAsync(me.RequiredId, ct);

    [HttpPost]
    public async Task<IActionResult> Submit(SubmitLeaveRequest r, CancellationToken ct)
    {
        await leaves.SubmitAsync(me.RequiredId, me.RequiredId, r, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await leaves.CancelAsync(id, me.RequiredId, ct);
        return NoContent();
    }
}
