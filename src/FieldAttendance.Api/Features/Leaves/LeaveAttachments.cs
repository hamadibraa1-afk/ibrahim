using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Leaves;

public sealed record LeaveAttachmentDto(Guid Id, string FileName, string ContentType, int Size, DateTimeOffset CreatedAt);

/// <summary>
/// Documents behind a leave request. They are often medical, so the circle is narrow: the employee,
/// the people in that leave's approval chain, and HR. Anyone else is told the request does not exist.
/// </summary>
public sealed class LeaveAttachmentService(AppDbContext db, AccessScope scope, ICurrentUser me)
{
    public async Task<LeaveAttachmentDto> AddAsync(Guid leaveId, IFormFile? file, CancellationToken ct)
    {
        var leave = await OwnPendingAsync(leaveId, ct);
        if (file is null || file.Length == 0)
            throw new DomainException("attachment.empty", "Choose a file to attach.");
        if (file.Length > LeaveAttachment.MaxBytes)
            throw new DomainException("attachment.too_large", "The file is larger than 5 MB.");
        if (await db.LeaveAttachments.CountAsync(a => a.LeaveRequestId == leave.Id && a.IsActive, ct) >= LeaveAttachment.MaxPerLeave)
            throw new DomainException("attachment.too_many", "A leave request can have at most 5 files.");

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var attachment = new LeaveAttachment(leave.Id, file.FileName, buffer.ToArray(), me.RequiredId);
        db.LeaveAttachments.Add(attachment);
        await db.SaveChangesAsync(ct);
        return new LeaveAttachmentDto(attachment.Id, attachment.FileName, attachment.ContentType, attachment.Size, attachment.CreatedAt);
    }

    /// <summary>Only while the request is pending: once decided, the documents are part of the record.</summary>
    public async Task RemoveAsync(Guid leaveId, Guid attachmentId, CancellationToken ct)
    {
        var leave = await OwnPendingAsync(leaveId, ct);
        var attachment = await db.LeaveAttachments.SingleOrDefaultAsync(a => a.Id == attachmentId && a.LeaveRequestId == leave.Id && a.IsActive, ct)
            ?? throw new DomainException("attachment.not_found", "File not found.");
        attachment.Deactivate();
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<LeaveAttachmentDto>> ListAsync(Guid leaveId, CancellationToken ct)
    {
        await EnsureCanViewAsync(leaveId, ct);
        return await db.LeaveAttachments.AsNoTracking().Where(a => a.LeaveRequestId == leaveId && a.IsActive)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new LeaveAttachmentDto(a.Id, a.FileName, a.ContentType, a.Size, a.CreatedAt)).ToListAsync(ct);
    }

    public async Task<LeaveAttachment> ContentAsync(Guid leaveId, Guid attachmentId, CancellationToken ct)
    {
        await EnsureCanViewAsync(leaveId, ct);
        return await db.LeaveAttachments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == attachmentId && a.LeaveRequestId == leaveId && a.IsActive, ct)
            ?? throw new DomainException("attachment.not_found", "File not found.");
    }

    private async Task EnsureCanViewAsync(Guid leaveId, CancellationToken ct)
    {
        var employeeId = await db.LeaveRequests.AsNoTracking().Where(l => l.Id == leaveId && l.IsActive)
            .Select(l => (Guid?)l.EmployeeId).SingleOrDefaultAsync(ct);
        if (employeeId is { } owner && me.Id is { } caller)
        {
            if (owner == caller || scope.SeesEveryone) return;
            if (await db.ApprovalSteps.AnyAsync(s => s.Kind == RequestKind.Leave && s.RequestId == leaveId && s.ApproverId == caller, ct)) return;
        }
        throw new DomainException("request.not_found", "Request not found.");
    }

    private async Task<LeaveRequest> OwnPendingAsync(Guid leaveId, CancellationToken ct)
    {
        var leave = await db.LeaveRequests.AsNoTracking().SingleOrDefaultAsync(l => l.Id == leaveId && l.IsActive, ct);
        if (leave is null || leave.EmployeeId != me.Id)
            throw new DomainException("request.not_found", "Request not found.");
        if (leave.Status != RequestStatus.Pending)
            throw new DomainException("attachment.decided", "Files can only be changed while the request is pending.");
        return leave;
    }
}

/// <summary>The employee's own side: add and remove documents on their pending leave.</summary>
[ApiController]
[Route("api/me/leaves/{leaveId:guid}/attachments")]
[Authorize(Policy = Policies.SelfService)]
public sealed class MyLeaveAttachmentsController(LeaveAttachmentService attachments) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(LeaveAttachment.MaxBytes + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = LeaveAttachment.MaxBytes + 64 * 1024)]
    public Task<LeaveAttachmentDto> Add(Guid leaveId, IFormFile? file, CancellationToken ct) => attachments.AddAsync(leaveId, file, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid leaveId, Guid id, CancellationToken ct)
    {
        await attachments.RemoveAsync(leaveId, id, ct);
        return NoContent();
    }
}

/// <summary>Viewing documents: the employee, the approvers of that leave, and HR.</summary>
[ApiController]
[Route("api/leaves/{leaveId:guid}/attachments")]
[Authorize]
public sealed class LeaveAttachmentsController(LeaveAttachmentService attachments) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<LeaveAttachmentDto>> List(Guid leaveId, CancellationToken ct) => attachments.ListAsync(leaveId, ct);

    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> Content(Guid leaveId, Guid id, CancellationToken ct)
    {
        var file = await attachments.ContentAsync(leaveId, id, ct);
        // Personal documents: never cached, and never re-interpreted as another type by the browser.
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(file.Content, file.ContentType, file.FileName);
    }
}
