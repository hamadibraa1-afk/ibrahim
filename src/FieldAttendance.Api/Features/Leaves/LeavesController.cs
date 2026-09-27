using System.ComponentModel.DataAnnotations;
using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Api.Features.Approvals;
using FieldAttendance.Api.Features.Requests;
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

/// <summary>
/// The leave inbox for every approver: supervisors, section heads, line and department managers, HR.
/// Approvers are people rather than roles, so the gate is the chain itself: only the person a leave
/// is waiting on may sign it.
/// </summary>
[ApiController]
[Route("api/requests/leaves")]
[Authorize(Policy = Policies.Approver)]
public sealed class LeaveRequestsController(LeaveService leaves, RequestInbox inbox, ICurrentUser me) : ControllerBase
{
    /// <summary>Pending: the ones waiting on this approver. Decided: the reviewer's view, or only what the caller decided.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<LeaveDto>> List([FromQuery] string status = "Pending", [FromQuery] string? workforce = null,
        CancellationToken ct = default)
    {
        var parsed = Enum.TryParse<RequestStatus>(status, true, out var s) ? s : RequestStatus.Pending;
        var list = await leaves.ListAsync(null, parsed, ct);
        if (inbox.EmployeesOf(workforce) is { } side)
        {
            var ids = side.ToHashSet();
            list = list.Where(l => ids.Contains(l.EmployeeId)).ToList();
        }

        if (parsed == RequestStatus.Pending)
        {
            var waiting = await inbox.WaitingAsync(RequestKind.Leave, ct);
            return list.Where(l => waiting.Contains(l.Id)).ToList();
        }
        return RequestInbox.ReviewsHistory(User) ? list : list.Where(l => l.DecidedById == me.RequiredId).ToList();
    }

    [HttpGet("{id:guid}/timeline")]
    public Task<IReadOnlyList<ApprovalStepView>> Timeline(Guid id, CancellationToken ct) => leaves.TimelineAsync(id, null, ct);

    [HttpPost]
    [Authorize(Policy = Policies.Manage)]
    public async Task<IActionResult> SubmitOnBehalf(OnBehalfLeaveRequest r, CancellationToken ct)
    {
        await leaves.SubmitAsync(r.EmployeeId, me.RequiredId, new SubmitLeaveRequest(r.LeaveTypeId, r.FromDate, r.ToDate, r.Reason), ct);
        return NoContent();
    }

    /// <summary>
    /// Open to any approver: a department manager or HR signing their step was refused here by a
    /// supervisors-only policy, which left office leave stuck at the second level.
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        await leaves.DecideAsync(id, me.RequiredId, approve: true, null, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, RejectLeaveRequest r, CancellationToken ct)
    {
        await leaves.DecideAsync(id, me.RequiredId, approve: false, r.Reason, ct);
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
[Authorize(Policy = Policies.SelfService)]
public sealed class MyLeavesController(LeaveService leaves, ICurrentUser me, SelfServiceScope self) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<LeaveDto>> Mine(CancellationToken ct) => leaves.ListAsync(me.RequiredId, null, ct);

    /// <summary>Who has signed and who holds the request now.</summary>
    [HttpGet("{id:guid}/timeline")]
    public Task<IReadOnlyList<ApprovalStepView>> Timeline(Guid id, CancellationToken ct) => leaves.TimelineAsync(id, me.RequiredId, ct);

    /// <summary>
    /// Balances are synthesised from the leave types, so without this check an account with no
    /// employment record would be shown an allowance it can never take.
    /// </summary>
    [HttpGet("balances")]
    public async Task<IReadOnlyList<LeaveBalanceDto>> Balances(CancellationToken ct) =>
        await self.HasEmploymentRecordAsync(ct) ? await leaves.BalancesAsync(me.RequiredId, ct) : [];

    [HttpPost]
    public async Task<IActionResult> Submit(SubmitLeaveRequest r, CancellationToken ct)
    {
        // Leave, unlike a permission, is not tied to a scheduled shift, so nothing else stops an
        // account with no employment record from filing one into the approvers' inbox.
        await self.EnsureEmploymentRecordAsync(ct);
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
