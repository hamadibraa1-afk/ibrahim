using FieldAttendance.Domain.Common;

namespace FieldAttendance.Domain.Entities;

/// <summary>
/// A fixed monthly amount paid on top of the basic, such as housing or transport. It is its own
/// payslip line and never part of the basic, so deductions (priced from the basic) do not grow with it.
/// </summary>
public sealed class SalaryAllowance : Entity
{
    private SalaryAllowance() { } // EF Core

    public SalaryAllowance(Guid employeeId, string name, decimal monthlyAmount, DateOnly fromDate, DateOnly? toDate)
    {
        EmployeeId = Guard.NotEmpty(employeeId, "salary_allowance.employee");
        Name = Guard.Required(name, "salary_allowance.name", 100);
        if (monthlyAmount is <= 0 or > 1_000_000)
            throw new DomainException("salary_allowance.amount", "Monthly amount is out of range.");
        if (toDate is { } end && end < fromDate)
            throw new DomainException("salary_allowance.end_before_start", "End date cannot be before start date.");
        MonthlyAmount = monthlyAmount;
        FromDate = fromDate;
        ToDate = toDate;
    }

    public Guid EmployeeId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public decimal MonthlyAmount { get; private set; }
    public DateOnly FromDate { get; private set; }

    /// <summary>Last day paid; null while it continues.</summary>
    public DateOnly? ToDate { get; private set; }

    /// <summary>Stops the allowance after <paramref name="lastDay"/>. It cannot end before it started.</summary>
    public void EndOn(DateOnly lastDay)
    {
        if (lastDay < FromDate)
            throw new DomainException("salary_allowance.end_before_start", "End date cannot be before start date.");
        ToDate = lastDay;
    }

    /// <summary>
    /// The amount for a pay period: the full monthly figure when it covers the whole period,
    /// otherwise the share of the period's calendar days it covers.
    /// </summary>
    public decimal AmountFor(DateOnly first, DateOnly last)
    {
        var start = FromDate > first ? FromDate : first;
        var end = ToDate is { } to && to < last ? to : last;
        if (end < start) return 0m;

        var covered = end.DayNumber - start.DayNumber + 1;
        var total = last.DayNumber - first.DayNumber + 1;
        return Math.Round(MonthlyAmount * covered / total, 2, MidpointRounding.AwayFromZero);
    }
}
