using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;

namespace FieldAttendance.Domain.Entities;

/// <summary>
/// Shared approval workflow for permissions, leaves and attendance exceptions.
/// A decision is final: the first supervisor to act wins; the RowVersion check in
/// persistence rejects a second concurrent decision.
/// </summary>
public abstract class ApprovableRequest : Entity
{
    protected ApprovableRequest() { }

    protected ApprovableRequest(Guid employeeId, string? reason, Guid submittedByUserId, bool reasonRequired)
    {
        EmployeeId = Guard.NotEmpty(employeeId, "request.employee");
        SubmittedByUserId = Guard.NotEmpty(submittedByUserId, "request.submitted_by");
        Reason = reasonRequired ? Guard.Required(reason, "request.reason", 500) : Guard.Optional(reason, "request.reason", 500);
    }

    public Guid EmployeeId { get; private set; }
    public string? Reason { get; private set; }
    public RequestStatus Status { get; private set; } = RequestStatus.Pending;
    public Guid SubmittedByUserId { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? RejectReason { get; private set; }

    public bool IsSubmittedOnBehalf => SubmittedByUserId != EmployeeId;

    public void Approve(Guid supervisorId, DateTimeOffset at)
    {
        EnsurePending();
        Status = RequestStatus.Approved;
        DecidedBy = Guard.NotEmpty(supervisorId, "request.decided_by");
        DecidedAt = at;
    }

    public void Reject(Guid supervisorId, DateTimeOffset at, string reason)
    {
        EnsurePending();
        RejectReason = Guard.Required(reason, "request.reject_reason", 500);
        Status = RequestStatus.Rejected;
        DecidedBy = Guard.NotEmpty(supervisorId, "request.decided_by");
        DecidedAt = at;
    }

    protected void MarkCancelled(bool allowApproved)
    {
        var cancellable = Status == RequestStatus.Pending || (allowApproved && Status == RequestStatus.Approved);
        if (!cancellable)
            throw new DomainException("request.not_cancellable", "This request can no longer be cancelled.");
        Status = RequestStatus.Cancelled;
    }

    private void EnsurePending()
    {
        if (Status != RequestStatus.Pending)
            throw new DomainException("request.already_decided", "This request has already been decided.");
    }
}
