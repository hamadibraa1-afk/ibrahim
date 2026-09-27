using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using Xunit;

namespace FieldAttendance.Domain.Tests;

public class ExtraPaymentTests
{
    private static readonly Guid Employee = Guid.NewGuid();

    [Fact]
    public void A_bonus_and_an_overtime_payment_keep_what_was_decided()
    {
        var bonus = new ExtraPayment(Employee, 2026, 9, ExtraPaymentKind.Bonus, 1000m, null, "تميز في الحملة");
        var overtime = new ExtraPayment(Employee, 2026, 9, ExtraPaymentKind.Overtime, 260.42m, 600, "حملة رمضان");

        Assert.Equal(1000m, bonus.Amount);
        Assert.Null(bonus.OvertimeMinutes);
        Assert.Equal(600, overtime.OvertimeMinutes);
        Assert.Equal(new DateOnly(2026, 9, 1), overtime.FirstDay);
    }

    [Theory]
    [InlineData(ExtraPaymentKind.Bonus, 0, null)]
    [InlineData(ExtraPaymentKind.Bonus, 500, 60)]
    [InlineData(ExtraPaymentKind.Overtime, 100, 0)]
    [InlineData((ExtraPaymentKind)9, 100, null)]
    public void Nonsense_is_refused(ExtraPaymentKind kind, decimal amount, int? minutes) =>
        Assert.Throws<DomainException>(() => new ExtraPayment(Employee, 2026, 9, kind, amount, minutes, "سبب"));

    [Fact]
    public void A_payment_needs_a_reason() =>
        Assert.Throws<DomainException>(() => new ExtraPayment(Employee, 2026, 9, ExtraPaymentKind.Bonus, 100m, null, " "));
}
