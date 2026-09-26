using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;

namespace FieldAttendance.Domain.Entities;

public sealed class LeaveType : Entity
{
    private LeaveType() { } // EF Core

    public LeaveType(string nameAr, string nameEn, int? annualBalanceDays) => Update(nameAr, nameEn, annualBalanceDays);

    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;

    /// <summary>Null = unlimited (no balance tracking).</summary>
    public int? AnnualBalanceDays { get; private set; }

    public void Update(string nameAr, string nameEn, int? annualBalanceDays)
    {
        NameAr = Guard.Required(nameAr, "leave_type.name_ar", 100);
        NameEn = Guard.Required(nameEn, "leave_type.name_en", 100);
        AnnualBalanceDays = annualBalanceDays is { } days ? Guard.InRange(days, 0, 366, "leave_type.balance") : null;
    }
}

public sealed class LeaveBalance : Entity
{
    private LeaveBalance() { } // EF Core

    public LeaveBalance(Guid employeeId, Guid leaveTypeId, int year, int? totalDays)
    {
        EmployeeId = Guard.NotEmpty(employeeId, "leave_balance.employee");
        LeaveTypeId = Guard.NotEmpty(leaveTypeId, "leave_balance.type");
        Year = Guard.InRange(year, 2000, 2100, "leave_balance.year");
        TotalDays = totalDays is { } t ? Guard.InRange(t, 0, 366, "leave_balance.total") : null;
    }

    public Guid EmployeeId { get; private set; }
    public Guid LeaveTypeId { get; private set; }
    public int Year { get; private set; }
    public int? TotalDays { get; private set; }
    public int UsedDays { get; private set; }

    public int? RemainingDays => TotalDays - UsedDays;

    public void Deduct(int days)
    {
        Guard.InRange(days, 1, 366, "leave_balance.days");
        if (TotalDays is { } total && UsedDays + days > total)
            throw new DomainException("leave_balance.insufficient", "Insufficient leave balance.");
        UsedDays += days;
    }

    public void Restore(int days) => UsedDays = Math.Max(0, UsedDays - Guard.InRange(days, 1, 366, "leave_balance.days"));

    /// <summary>Manual adjustment from the employee profile; the reason is captured in the audit log.</summary>
    public void AdjustTotal(int? totalDays)
    {
        if (totalDays is { } t && t < UsedDays)
            throw new DomainException("leave_balance.below_used", "Total cannot be less than days already used.");
        TotalDays = totalDays;
    }
}

public sealed class LeaveRequest : ApprovableRequest
{
    private LeaveRequest() { } // EF Core

    /// <param name="scheduledWorkingDays">
    /// Count of the employee's scheduled working days in the range (from ScheduleResolver).
    /// Only these days are deducted from the balance; rest days are free.
    /// </param>
    public LeaveRequest(Guid employeeId, Guid leaveTypeId, DateOnly fromDate, DateOnly toDate, int scheduledWorkingDays,
        string? reason, Guid submittedBy)
        : base(employeeId, reason, submittedBy, reasonRequired: false)
    {
        if (toDate < fromDate)
            throw new DomainException("leave.end_before_start", "End date cannot be before start date.");
        if (scheduledWorkingDays <= 0)
            throw new DomainException("leave.no_working_days", "The selected period contains no scheduled working days.");
        LeaveTypeId = Guard.NotEmpty(leaveTypeId, "leave.type");
        FromDate = fromDate;
        ToDate = toDate;
        WorkingDays = scheduledWorkingDays;
    }

    public Guid LeaveTypeId { get; private set; }
    public DateOnly FromDate { get; private set; }
    public DateOnly ToDate { get; private set; }
    public int WorkingDays { get; private set; }

    public bool CoversApproved(DateOnly date) =>
        IsActive && Status == RequestStatus.Approved && date >= FromDate && date <= ToDate;

    /// <summary>Supervisors can cancel an approved leave; the caller restores the balance.</summary>
    public void Cancel() => MarkCancelled(allowApproved: true);
}
