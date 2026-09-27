using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;

namespace FieldAttendance.Domain.Entities;

/// <summary>
/// The approval chain for one kind of request, configured by HR. Changing the chain is a
/// settings change, not a code change: add a level, remove one, or reorder them.
/// </summary>
public sealed class ApprovalFlow : Entity
{
    private readonly List<ApprovalFlowLevel> _levels = [];

    private ApprovalFlow() { } // EF Core

    public ApprovalFlow(RequestKind kind, string nameAr, string nameEn, int escalationHours, Workforce? workforce = null)
    {
        Kind = kind;
        Workforce = workforce;
        Rename(nameAr, nameEn);
        SetEscalation(escalationHours);
    }

    public RequestKind Kind { get; private set; }

    /// <summary>
    /// The workforce this chain is for; null is the chain for anyone without one of their own.
    /// Field requests go supervisor, then the supervisor's department manager, which is not the
    /// office chain, so the two are configured separately.
    /// </summary>
    public Workforce? Workforce { get; private set; }
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;

    /// <summary>Hours before an undecided level is escalated to the one above it. 0 = never.</summary>
    public int EscalationHours { get; private set; }

    public IReadOnlyCollection<ApprovalFlowLevel> Levels => _levels.AsReadOnly();

    public void Rename(string nameAr, string nameEn)
    {
        NameAr = Guard.Required(nameAr, "flow.name_ar", 120);
        NameEn = Guard.Required(nameEn, "flow.name_en", 120);
    }

    public void SetEscalation(int hours) => EscalationHours = Guard.InRange(hours, 0, 720, "flow.escalation");

    /// <summary>Replaces the chain in one go; the order given is the order of signing.</summary>
    public void SetLevels(IReadOnlyList<ApprovalStage> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        if (stages.Count == 0)
            throw new DomainException("flow.no_levels", "An approval chain needs at least one level.");
        if (stages.Distinct().Count() != stages.Count)
            throw new DomainException("flow.duplicate_level", "The same level cannot appear twice in one chain.");

        _levels.Clear();
        for (var i = 0; i < stages.Count; i++) _levels.Add(new ApprovalFlowLevel(Id, i + 1, stages[i]));
    }

    public IReadOnlyList<ApprovalStage> Stages() => _levels.OrderBy(l => l.Order).Select(l => l.Stage).ToList();
}

public sealed class ApprovalFlowLevel : Entity
{
    private ApprovalFlowLevel() { } // EF Core

    internal ApprovalFlowLevel(Guid approvalFlowId, int order, ApprovalStage stage)
    {
        ApprovalFlowId = approvalFlowId;
        Order = Guard.InRange(order, 1, 10, "flow.order");
        Stage = stage;
    }

    public Guid ApprovalFlowId { get; private set; }
    public int Order { get; private set; }
    public ApprovalStage Stage { get; private set; }
}

/// <summary>
/// One signature on the way. Steps are created when the request is submitted, so the employee
/// can see exactly who holds it now and who signed before.
/// </summary>
public sealed class ApprovalStep : Entity
{
    private ApprovalStep() { } // EF Core

    public ApprovalStep(RequestKind kind, Guid requestId, Guid employeeId, int order, ApprovalStage stage, Guid? approverId)
    {
        Kind = kind;
        RequestId = Guard.NotEmpty(requestId, "approval.request");
        EmployeeId = Guard.NotEmpty(employeeId, "approval.employee");
        Order = Guard.InRange(order, 1, 10, "approval.order");
        Stage = stage;
        ApproverId = approverId;
    }

    public RequestKind Kind { get; private set; }
    public Guid RequestId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public int Order { get; private set; }
    public ApprovalStage Stage { get; private set; }

    /// <summary>The person expected to sign. Null when nobody fills that role, and the step is skipped.</summary>
    public Guid? ApproverId { get; private set; }

    public ApprovalStepStatus Status { get; private set; } = ApprovalStepStatus.Pending;
    public Guid? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? Note { get; private set; }

    public bool IsOpen => Status == ApprovalStepStatus.Pending;

    public void Approve(Guid decidedBy, DateTimeOffset at, string? note)
    {
        EnsureOpen();
        Status = ApprovalStepStatus.Approved;
        Record(decidedBy, at, note);
    }

    public void Reject(Guid decidedBy, DateTimeOffset at, string reason)
    {
        EnsureOpen();
        Status = ApprovalStepStatus.Rejected;
        Record(decidedBy, at, Guard.Required(reason, "approval.reason", 500));
    }

    /// <summary>No one holds this role for this employee, so the chain moves on without a gap.</summary>
    public void Skip(string reason)
    {
        EnsureOpen();
        Status = ApprovalStepStatus.Skipped;
        Note = Guard.Optional(reason, "approval.reason", 500);
    }

    public void Reassign(Guid approverId) 
    {
        EnsureOpen();
        ApproverId = Guard.NotEmpty(approverId, "approval.approver");
    }

    private void Record(Guid decidedBy, DateTimeOffset at, string? note)
    {
        DecidedBy = Guard.NotEmpty(decidedBy, "approval.decided_by");
        DecidedAt = at;
        Note = Guard.Optional(note, "approval.note", 500);
    }

    private void EnsureOpen()
    {
        if (!IsOpen) throw new DomainException("approval.already_decided", "This step has already been decided.");
    }
}

/// <summary>
/// Returning to work after leave. It is a step in its own right: the employee reports back,
/// the manager confirms, and a late return is visible instead of silently becoming absence.
/// </summary>
public sealed class ReturnToWork : Entity
{
    private ReturnToWork() { } // EF Core

    public ReturnToWork(Guid employeeId, Guid leaveRequestId, DateOnly expectedDate)
    {
        EmployeeId = Guard.NotEmpty(employeeId, "return.employee");
        LeaveRequestId = Guard.NotEmpty(leaveRequestId, "return.leave");
        ExpectedDate = expectedDate;
    }

    public Guid EmployeeId { get; private set; }
    public Guid LeaveRequestId { get; private set; }
    public DateOnly ExpectedDate { get; private set; }
    public DateOnly? ActualDate { get; private set; }
    public ReturnStatus Status { get; private set; } = ReturnStatus.Expected;
    public string? Note { get; private set; }
    public Guid? ConfirmedBy { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }

    public int LateDays => ActualDate is { } actual && actual > ExpectedDate ? actual.DayNumber - ExpectedDate.DayNumber : 0;

    public void Report(DateOnly actualDate, string? note)
    {
        if (Status == ReturnStatus.Confirmed)
            throw new DomainException("return.already_confirmed", "This return has already been confirmed.");
        ActualDate = actualDate;
        Note = Guard.Optional(note, "return.note", 500);
        Status = actualDate > ExpectedDate ? ReturnStatus.Late : ReturnStatus.Submitted;
    }

    public void Confirm(Guid managerId, DateTimeOffset at)
    {
        if (ActualDate is null)
            throw new DomainException("return.not_reported", "The employee has not reported back yet.");
        ConfirmedBy = Guard.NotEmpty(managerId, "return.manager");
        ConfirmedAt = at;
        Status = ReturnStatus.Confirmed;
    }

    /// <summary>True once the expected day has passed with no report: the manager should be alerted.</summary>
    public bool IsOverdue(DateOnly today) => ActualDate is null && today > ExpectedDate;
}
