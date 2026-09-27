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

public sealed record LeaveTypeDto(Guid Id, string NameAr, string NameEn, int? AnnualBalanceDays, bool IsActive,
    bool IsPaid, bool RequiresAttachment, byte[] RowVersion);

public sealed record SaveLeaveTypeRequest([Required] string NameAr, [Required] string NameEn, [Range(0, 366)] int? AnnualBalanceDays,
    bool IsPaid, bool RequiresAttachment, byte[]? RowVersion = null);

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
            .Select(t => ToDto(t)).ToListAsync(ct);

    internal static LeaveTypeDto ToDto(Domain.Entities.LeaveType t) =>
        new(t.Id, t.NameAr, t.NameEn, t.AnnualBalanceDays, t.IsActive, t.IsPaid, t.RequiresAttachment, t.RowVersion);
}

/// <summary>
/// HR defines the leave types: their yearly allowance, whether the salary continues during them,
/// and whether a supporting document is required. Types in use are deactivated, never deleted.
/// </summary>
[ApiController]
[Route("api/hr/leave-types")]
[Authorize(Policy = HrPolicies.Read)]
public sealed class HrLeaveTypesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<LeaveTypeDto>> List([FromQuery] bool includeInactive, CancellationToken ct) =>
        (await db.LeaveTypes.AsNoTracking().Where(t => includeInactive || t.IsActive).OrderBy(t => t.NameAr).ToListAsync(ct))
            .Select(LeaveTypesController.ToDto).ToList();

    [HttpPost]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<ActionResult<LeaveTypeDto>> Create(SaveLeaveTypeRequest r, CancellationToken ct)
    {
        var type = new Domain.Entities.LeaveType(r.NameAr, r.NameEn, r.AnnualBalanceDays, r.IsPaid, r.RequiresAttachment);
        db.LeaveTypes.Add(type);
        await db.SaveChangesAsync(ct);
        return LeaveTypesController.ToDto(type);
    }

    /// <summary>
    /// A change to "paid" applies to every month not yet approved; approved and closed months keep
    /// the payslips they were settled with.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<ActionResult<LeaveTypeDto>> Update(Guid id, SaveLeaveTypeRequest r, CancellationToken ct)
    {
        var type = await Find(id, ct);
        db.ExpectVersion(type, r.RowVersion);
        type.Update(r.NameAr, r.NameEn, r.AnnualBalanceDays, r.IsPaid, r.RequiresAttachment);
        await db.SaveChangesAsync(ct);
        return LeaveTypesController.ToDto(type);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var type = await Find(id, ct);
        type.Deactivate();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
    {
        var type = await Find(id, ct);
        type.Activate();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<Domain.Entities.LeaveType> Find(Guid id, CancellationToken ct) =>
        await db.LeaveTypes.SingleOrDefaultAsync(t => t.Id == id, ct)
        ?? throw new Domain.Common.DomainException("leave.type_not_found", "Leave type not found.");
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
        // The id lets the screen attach documents to the request straight after submitting it.
        var leave = await leaves.SubmitAsync(me.RequiredId, me.RequiredId, r, ct);
        return Ok(new { leave.Id });
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await leaves.CancelAsync(id, me.RequiredId, ct);
        return NoContent();
    }
}
