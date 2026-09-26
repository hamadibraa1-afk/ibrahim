using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;

namespace FieldAttendance.Domain.Entities;

/// <summary>
/// One month of payroll. It carries the policy it was calculated with, so reopening an old
/// cycle never re-prices it with today's rules, and closing it freezes the figures for good.
/// </summary>
public sealed class PayrollCycle : Entity
{
    private PayrollCycle() { }

    public PayrollCycle(int year, int month, int monthDays, int dailyWorkMinutes, decimal overtimeFactor,
        decimal maxDeductionPercent, Guid createdBy)
    {
        Year = Guard.InRange(year, 2020, 2100, "payroll.year");
        Month = Guard.InRange(month, 1, 12, "payroll.month");
        MonthDays = Guard.InRange(monthDays, 28, 31, "payroll.month_days");
        DailyWorkMinutes = Guard.InRange(dailyWorkMinutes, 60, 960, "payroll.daily_minutes");
        OvertimeFactor = overtimeFactor;
        MaxDeductionPercent = maxDeductionPercent;
        CreatedByUserId = Guard.NotEmpty(createdBy, "payroll.created_by");
    }

    public int Year { get; private set; }
    public int Month { get; private set; }
    public int MonthDays { get; private set; }
    public int DailyWorkMinutes { get; private set; }
    public decimal OvertimeFactor { get; private set; }
    public decimal MaxDeductionPercent { get; private set; }

    public PayrollStatus Status { get; private set; } = PayrollStatus.Draft;
    public Guid CreatedByUserId { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public DateTimeOffset? CalculatedAt { get; private set; }

    public DateOnly FirstDay => new(Year, Month, 1);
    public DateOnly LastDay => new(Year, Month, DateTime.DaysInMonth(Year, Month));
    public bool IsOpen => Status is PayrollStatus.Draft or PayrollStatus.Review;

    public void EnsureOpen()
    {
        if (!IsOpen)
            throw new DomainException("payroll.locked", "This cycle is approved or closed and cannot be recalculated.");
    }

    public void MarkCalculated(DateTimeOffset at) { EnsureOpen(); CalculatedAt = at; }

    public void SendToReview() { EnsureOpen(); Status = PayrollStatus.Review; }

    public void Approve(Guid approvedBy, DateTimeOffset at)
    {
        EnsureOpen();
        if (CalculatedAt is null)
            throw new DomainException("payroll.not_calculated", "Calculate the cycle before approving it.");
        ApprovedBy = Guard.NotEmpty(approvedBy, "payroll.approved_by");
        ApprovedAt = at;
        Status = PayrollStatus.Approved;
    }

    public void Close(DateTimeOffset at)
    {
        if (Status != PayrollStatus.Approved)
            throw new DomainException("payroll.not_approved", "Approve the cycle before closing it.");
        ClosedAt = at;
        Status = PayrollStatus.Closed;
    }

    /// <summary>Only an administrator reopens a closed cycle, and the reason is recorded by the caller.</summary>
    public void Reopen()
    {
        if (Status != PayrollStatus.Closed && Status != PayrollStatus.Approved)
            throw new DomainException("payroll.not_closed", "This cycle is already open.");
        Status = PayrollStatus.Draft;
        ClosedAt = null;
        ApprovedAt = null;
        ApprovedBy = null;
    }
}

/// <summary>One employee's payslip inside a cycle, with the lines that make it up.</summary>
public sealed class PayrollLine : Entity
{
    private readonly List<PayrollLineItem> _items = [];

    private PayrollLine() { }

    public PayrollLine(Guid payrollCycleId, Guid employeeId, decimal basicSalary)
    {
        PayrollCycleId = Guard.NotEmpty(payrollCycleId, "payroll.cycle");
        EmployeeId = Guard.NotEmpty(employeeId, "payroll.employee");
        BasicSalary = basicSalary;
    }

    public Guid PayrollCycleId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public decimal BasicSalary { get; private set; }

    public int ScheduledDays { get; private set; }
    public int PresentDays { get; private set; }
    public int AbsentDays { get; private set; }
    public int LeaveDays { get; private set; }
    public int UnpaidLeaveDays { get; private set; }
    public int LateMinutes { get; private set; }
    public int OvertimeMinutes { get; private set; }

    public decimal Earnings { get; private set; }
    public decimal Deductions { get; private set; }
    public decimal CappedDeductions { get; private set; }
    public decimal NetPay { get; private set; }

    public IReadOnlyCollection<PayrollLineItem> Items => _items.AsReadOnly();

    public void SetAttendance(int scheduledDays, int presentDays, int absentDays, int leaveDays, int unpaidLeaveDays,
        int lateMinutes, int overtimeMinutes)
    {
        ScheduledDays = scheduledDays;
        PresentDays = presentDays;
        AbsentDays = absentDays;
        LeaveDays = leaveDays;
        UnpaidLeaveDays = unpaidLeaveDays;
        LateMinutes = lateMinutes;
        OvertimeMinutes = overtimeMinutes;
    }

    public void ClearItems() => _items.Clear();

    public PayrollLineItem AddItem(string label, decimal amount, bool isDeduction, string? sourceKey = null)
    {
        var item = new PayrollLineItem(Id, label, decimal.Round(amount, 2), isDeduction, sourceKey);
        _items.Add(item);
        return item;
    }

    /// <summary>Totals the lines. <paramref name="cappedDeductions"/> is what policy allows to be taken.</summary>
    public void Total(decimal cappedDeductions)
    {
        Earnings = decimal.Round(_items.Where(i => !i.IsDeduction).Sum(i => i.Amount), 2);
        Deductions = decimal.Round(_items.Where(i => i.IsDeduction).Sum(i => i.Amount), 2);
        CappedDeductions = decimal.Round(cappedDeductions, 2);
        NetPay = Math.Max(0m, decimal.Round(Earnings - CappedDeductions, 2));
    }
}

public sealed class PayrollLineItem : Entity
{
    private PayrollLineItem() { }

    internal PayrollLineItem(Guid payrollLineId, string label, decimal amount, bool isDeduction, string? sourceKey)
    {
        PayrollLineId = payrollLineId;
        Label = Guard.Required(label, "payroll.item_label", 150);
        Amount = amount;
        IsDeduction = isDeduction;
        SourceKey = Guard.Optional(sourceKey, "payroll.item_source", 100);
    }

    public Guid PayrollLineId { get; private set; }
    public string Label { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public bool IsDeduction { get; private set; }

    /// <summary>Where the line came from, e.g. "deduction:{id}" — used to trace a payslip back.</summary>
    public string? SourceKey { get; private set; }
}
