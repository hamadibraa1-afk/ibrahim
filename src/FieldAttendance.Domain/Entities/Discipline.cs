using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;

namespace FieldAttendance.Domain.Entities;

/// <summary>
/// A kind of deduction HR can define: what triggers it, how much, and whether it may be
/// replaced by a warning. The amount is never a flat figure for everyone — it is a number
/// of days or hours, turned into money from each employee's own salary (see DeductionMath).
/// </summary>
public sealed class DeductionType : LookupEntity
{
    private DeductionType() { }

    public DeductionType(string nameAr, string nameEn, DeductionUnit unit, decimal amount, DeductionTrigger trigger,
        int? triggerThreshold, Guid? alternativeWarningLevelId, decimal? monthlyCapPercent, string? notes = null)
        : base(nameAr, nameEn, notes)
    {
        SetRules(unit, amount, trigger, triggerThreshold, alternativeWarningLevelId, monthlyCapPercent);
    }

    public DeductionUnit Unit { get; private set; }

    /// <summary>Days, hours, or a fixed amount, depending on <see cref="Unit"/>.</summary>
    public decimal Amount { get; private set; }

    public DeductionTrigger Trigger { get; private set; }

    /// <summary>Only propose after this many occurrences in the month. Null or 1 = every time.</summary>
    public int? TriggerThreshold { get; private set; }

    /// <summary>The warning HR may issue instead of taking money.</summary>
    public Guid? AlternativeWarningLevelId { get; private set; }

    /// <summary>Upper limit for this type as a share of monthly salary.</summary>
    public decimal? MonthlyCapPercent { get; private set; }

    public void SetRules(DeductionUnit unit, decimal amount, DeductionTrigger trigger, int? triggerThreshold,
        Guid? alternativeWarningLevelId, decimal? monthlyCapPercent)
    {
        if (amount <= 0 || amount > 100_000)
            throw new DomainException("deduction_type.amount", "Amount is out of range.");
        if (unit == DeductionUnit.Day && amount > 31)
            throw new DomainException("deduction_type.days", "A deduction cannot exceed 31 days.");
        if (triggerThreshold is { } t && (t < 1 || t > 31))
            throw new DomainException("deduction_type.threshold", "Threshold must be between 1 and 31.");
        if (monthlyCapPercent is { } cap && (cap <= 0 || cap > 100))
            throw new DomainException("deduction_type.cap", "Cap must be a percentage between 1 and 100.");

        Unit = unit;
        Amount = amount;
        Trigger = trigger;
        TriggerThreshold = triggerThreshold;
        AlternativeWarningLevelId = alternativeWarningLevelId;
        MonthlyCapPercent = monthlyCapPercent;
    }
}

/// <summary>A step on the disciplinary ladder: verbal note, first warning, final warning…</summary>
public sealed class WarningLevel : LookupEntity
{
    private WarningLevel() { }

    public WarningLevel(string nameAr, string nameEn, int order, int validityDays, string? template, string? notes = null)
        : base(nameAr, nameEn, notes)
    {
        SetOrder(order);
        SetValidity(validityDays);
        Template = Guard.Optional(template, "warning_level.template", 2000);
    }

    public int Order { get; private set; }

    /// <summary>After this many days the warning no longer counts towards escalation.</summary>
    public int ValidityDays { get; private set; }

    public string? Template { get; private set; }

    public void SetOrder(int order) { Order = Guard.InRange(order, 1, 20, "warning_level.order"); SetSortOrder(order); }
    public void SetValidity(int days) => ValidityDays = Guard.InRange(days, 1, 1095, "warning_level.validity");
    public void SetTemplate(string? template) => Template = Guard.Optional(template, "warning_level.template", 2000);
}

/// <summary>
/// A proposed deduction. The system only ever creates it in <see cref="DeductionStatus.Proposed"/>;
/// money is involved only after a person approves, and the amount is frozen at that moment so a
/// later salary change cannot rewrite a past payslip.
/// </summary>
public sealed class DeductionProposal : Entity
{
    private DeductionProposal() { }

    public DeductionProposal(Guid employeeId, Guid deductionTypeId, DateOnly onDate, decimal units,
        string reason, Guid? attendanceRecordId, Guid? proposedBy)
    {
        EmployeeId = Guard.NotEmpty(employeeId, "deduction.employee");
        DeductionTypeId = Guard.NotEmpty(deductionTypeId, "deduction.type");
        OnDate = onDate;
        Units = units > 0 ? units : throw new DomainException("deduction.units", "Units must be greater than zero.");
        Reason = Guard.Required(reason, "deduction.reason", 500);
        AttendanceRecordId = attendanceRecordId;
        ProposedBy = proposedBy;
    }

    public Guid EmployeeId { get; private set; }
    public Guid DeductionTypeId { get; private set; }
    public DateOnly OnDate { get; private set; }

    /// <summary>Days or hours, matching the type's unit.</summary>
    public decimal Units { get; private set; }

    public string Reason { get; private set; } = string.Empty;
    public Guid? AttendanceRecordId { get; private set; }
    public Guid? ProposedBy { get; private set; }

    public DeductionStatus Status { get; private set; } = DeductionStatus.Proposed;
    public Guid? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? DecisionNote { get; private set; }

    /// <summary>The money figure, frozen at approval time.</summary>
    public decimal? ApprovedAmount { get; private set; }
    public Guid? WarningId { get; private set; }

    /// <summary>Set once the deduction has been carried into a payroll cycle.</summary>
    public Guid? PayrollCycleId { get; private set; }

    public bool IsPending => Status == DeductionStatus.Proposed;

    public void Approve(Guid decidedBy, DateTimeOffset at, decimal amount, decimal? adjustedUnits, string? note)
    {
        EnsurePending();
        if (adjustedUnits is { } units)
            Units = units > 0 ? units : throw new DomainException("deduction.units", "Units must be greater than zero.");
        if (amount < 0)
            throw new DomainException("deduction.amount", "Amount cannot be negative.");

        ApprovedAmount = decimal.Round(amount, 2);
        Status = DeductionStatus.Approved;
        Decide(decidedBy, at, note);
    }

    public void ConvertToWarning(Guid decidedBy, DateTimeOffset at, Guid warningId, string? note)
    {
        EnsurePending();
        WarningId = Guard.NotEmpty(warningId, "deduction.warning");
        ApprovedAmount = null;
        Status = DeductionStatus.ConvertedToWarning;
        Decide(decidedBy, at, note);
    }

    public void Cancel(Guid decidedBy, DateTimeOffset at, string note)
    {
        EnsurePending();
        Status = DeductionStatus.Cancelled;
        Decide(decidedBy, at, Guard.Required(note, "deduction.note", 500));
    }

    /// <summary>Called when a payroll cycle takes this deduction, so it cannot be used twice.</summary>
    public void AttachToPayroll(Guid cycleId)
    {
        if (Status != DeductionStatus.Approved)
            throw new DomainException("deduction.not_approved", "Only an approved deduction enters payroll.");
        PayrollCycleId = cycleId;
    }

    public void DetachFromPayroll() => PayrollCycleId = null;

    private void Decide(Guid decidedBy, DateTimeOffset at, string? note)
    {
        DecidedBy = Guard.NotEmpty(decidedBy, "deduction.decided_by");
        DecidedAt = at;
        DecisionNote = Guard.Optional(note, "deduction.note", 500);
    }

    private void EnsurePending()
    {
        if (!IsPending)
            throw new DomainException("deduction.already_decided", "This proposal has already been decided.");
    }
}

/// <summary>A warning on the employee's record. No money, but it counts towards escalation.</summary>
public sealed class Warning : Entity
{
    private Warning() { }

    public Warning(Guid employeeId, Guid warningLevelId, string reason, Guid issuedBy, DateTimeOffset issuedAt, DateOnly? onDate)
    {
        EmployeeId = Guard.NotEmpty(employeeId, "warning.employee");
        WarningLevelId = Guard.NotEmpty(warningLevelId, "warning.level");
        Reason = Guard.Required(reason, "warning.reason", 1000);
        IssuedBy = Guard.NotEmpty(issuedBy, "warning.issued_by");
        IssuedAt = issuedAt;
        OnDate = onDate;
    }

    public Guid EmployeeId { get; private set; }
    public Guid WarningLevelId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid IssuedBy { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }
    public DateOnly? OnDate { get; private set; }

    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public string? Objection { get; private set; }
    public string? ObjectionResponse { get; private set; }

    public void Acknowledge(DateTimeOffset at) => AcknowledgedAt ??= at;

    public void FileObjection(string text, DateTimeOffset at)
    {
        if (Objection is not null)
            throw new DomainException("warning.already_objected", "An objection has already been filed.");
        Objection = Guard.Required(text, "warning.objection", 1000);
        Acknowledge(at);
    }

    public void RespondToObjection(string response) =>
        ObjectionResponse = Guard.Required(response, "warning.objection_response", 1000);

    /// <summary>Still counts towards escalation.</summary>
    public bool IsInForce(DateOnly today, int validityDays) =>
        IsActive && DateOnly.FromDateTime(IssuedAt.Date).AddDays(validityDays) >= today;
}
