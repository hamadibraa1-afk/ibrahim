using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;

namespace FieldAttendance.Domain.Payroll;

/// <param name="MonthDays">Days the monthly salary is divided by (30 by policy, or the real month length).</param>
/// <param name="DailyWorkMinutes">The employee's contracted minutes per day, used for hourly values.</param>
/// <param name="OvertimeFactor">Multiplier applied to the hourly rate for approved overtime.</param>
/// <param name="MaxDeductionPercent">Ceiling for all deductions together, as a share of monthly salary.</param>
public sealed record PayrollPolicy(int MonthDays, int DailyWorkMinutes, decimal OvertimeFactor, decimal MaxDeductionPercent)
{
    public static readonly PayrollPolicy Default = new(30, 480, 1.25m, 25m);

    public void Validate()
    {
        if (MonthDays is < 28 or > 31) throw new DomainException("payroll.month_days", "Month days must be 28–31.");
        if (DailyWorkMinutes is < 60 or > 960) throw new DomainException("payroll.daily_minutes", "Daily minutes are out of range.");
        if (OvertimeFactor is < 0 or > 5) throw new DomainException("payroll.overtime_factor", "Overtime factor is out of range.");
        if (MaxDeductionPercent is < 0 or > 100) throw new DomainException("payroll.cap", "Cap must be a percentage.");
    }
}

/// <summary>
/// Turns time into money for one employee. Everything here is proportional to that
/// employee's own salary: a day off a 5,000 salary is not a day off a 2,000 salary.
/// Pure arithmetic, no database, so it can be proven with tests.
/// </summary>
public static class PayrollMath
{
    /// <summary>Displayed daily value, rounded for reading.</summary>
    public static decimal DailyValue(decimal monthlySalary, PayrollPolicy policy) => Round(RawDaily(monthlySalary, policy));

    /// <summary>Displayed hourly value, rounded for reading.</summary>
    public static decimal HourlyValue(decimal monthlySalary, PayrollPolicy policy) => Round(RawHourly(monthlySalary, policy));

    // Money is always computed from the unrounded rate and rounded once at the end.
    // Rounding the rate first loses fils on every multiplication, which adds up across a payroll.
    private static decimal RawDaily(decimal monthlySalary, PayrollPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        return monthlySalary / policy.MonthDays;
    }

    private static decimal RawHourly(decimal monthlySalary, PayrollPolicy policy) =>
        RawDaily(monthlySalary, policy) / (policy.DailyWorkMinutes / 60m);

    /// <summary>Money for one deduction: units are days or hours, or the amount itself when fixed.</summary>
    public static decimal DeductionAmount(decimal monthlySalary, PayrollPolicy policy, DeductionUnit unit, decimal units) =>
        unit switch
        {
            DeductionUnit.Day => Round(RawDaily(monthlySalary, policy) * units),
            DeductionUnit.Hour => Round(RawHourly(monthlySalary, policy) * units),
            _ => Round(units),
        };

    public static decimal OvertimeAmount(decimal monthlySalary, PayrollPolicy policy, int overtimeMinutes)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (overtimeMinutes <= 0) return 0m;
        return Round(RawHourly(monthlySalary, policy) * (overtimeMinutes / 60m) * policy.OvertimeFactor);
    }

    public static decimal UnpaidLeaveAmount(decimal monthlySalary, PayrollPolicy policy, int unpaidDays) =>
        unpaidDays <= 0 ? 0m : Round(RawDaily(monthlySalary, policy) * unpaidDays);

    /// <summary>
    /// Applies the ceiling on total deductions. Returns what may actually be taken this month,
    /// so an employee never loses more of their salary than policy allows.
    /// </summary>
    public static decimal CapDeductions(decimal monthlySalary, PayrollPolicy policy, decimal requested)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        if (requested <= 0) return 0m;
        var ceiling = Round(monthlySalary * policy.MaxDeductionPercent / 100m);
        return Math.Min(Round(requested), ceiling);
    }

    /// <summary>Net pay never goes below zero: an excess is carried by the caller, not silently reversed.</summary>
    public static decimal Net(decimal earnings, decimal deductions) => Math.Max(0m, Round(earnings - deductions));

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
