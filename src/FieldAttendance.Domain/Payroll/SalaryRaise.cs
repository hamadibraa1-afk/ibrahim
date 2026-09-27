using FieldAttendance.Domain.Common;

namespace FieldAttendance.Domain.Payroll;

public enum RaiseKind { Amount = 1, Percent = 2 }

/// <summary>A raise on the basic salary, given as an amount or a percentage of it.</summary>
public static class SalaryRaise
{
    public static decimal Apply(decimal basic, RaiseKind kind, decimal value)
    {
        if (value <= 0)
            throw new DomainException("salary.raise_positive", "A raise must be more than zero.");

        var raised = kind switch
        {
            RaiseKind.Amount => basic + value,
            // Above 100% is almost always a typo for an amount, so it is refused rather than paid.
            RaiseKind.Percent when value <= 100 => basic * (1 + value / 100m),
            RaiseKind.Percent => throw new DomainException("salary.raise_percent", "A percentage raise cannot exceed 100%."),
            _ => throw new DomainException("salary.raise_kind", "Unknown raise type."),
        };
        return Math.Round(raised, 2, MidpointRounding.AwayFromZero);
    }
}
