using FieldAttendance.Domain.Common;

namespace FieldAttendance.Domain.Entities;

public enum ExtraPaymentKind { Bonus = 1, Overtime = 2 }

/// <summary>
/// A one-off payment HR decides to make for a month: a bonus, or overtime. Overtime is not paid
/// just because it was recorded; someone decides to pay it. The amount is fixed when it is entered,
/// so a later salary change or rate setting does not rewrite a decision already made.
/// </summary>
public sealed class ExtraPayment : Entity
{
    private ExtraPayment() { } // EF Core

    public ExtraPayment(Guid employeeId, int year, int month, ExtraPaymentKind kind, decimal amount, int? overtimeMinutes, string reason)
    {
        EmployeeId = Guard.NotEmpty(employeeId, "extra_payment.employee");
        if (year is < 2020 or > 2100 || month is < 1 or > 12)
            throw new DomainException("extra_payment.month", "Pick a valid month.");
        if (!Enum.IsDefined(kind))
            throw new DomainException("extra_payment.kind", "Unknown payment type.");
        if (amount is <= 0 or > 1_000_000)
            throw new DomainException("extra_payment.amount", "Amount is out of range.");
        if (kind == ExtraPaymentKind.Bonus && overtimeMinutes is not null)
            throw new DomainException("extra_payment.bonus_hours", "A bonus has no hours.");
        if (overtimeMinutes is <= 0)
            throw new DomainException("extra_payment.hours", "Overtime hours must be more than zero.");

        Year = year;
        Month = month;
        Kind = kind;
        Amount = amount;
        OvertimeMinutes = overtimeMinutes;
        Reason = Guard.Required(reason, "extra_payment.reason", 300);
    }

    public Guid EmployeeId { get; private set; }
    public int Year { get; private set; }
    public int Month { get; private set; }
    public ExtraPaymentKind Kind { get; private set; }
    public decimal Amount { get; private set; }

    /// <summary>The hours paid, when overtime was entered as hours; null when entered as an amount.</summary>
    public int? OvertimeMinutes { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public DateOnly FirstDay => new(Year, Month, 1);
}
