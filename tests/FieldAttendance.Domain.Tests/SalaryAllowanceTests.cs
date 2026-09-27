using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using Xunit;

namespace FieldAttendance.Domain.Tests;

public class SalaryAllowanceTests
{
    private static readonly Guid Employee = Guid.NewGuid();
    private static readonly DateOnly SepFirst = new(2026, 9, 1), SepLast = new(2026, 9, 30);

    [Fact]
    public void A_full_month_pays_the_whole_amount()
    {
        var housing = new SalaryAllowance(Employee, "بدل سكن", 2000m, new DateOnly(2026, 1, 1), null);
        Assert.Equal(2000m, housing.AmountFor(SepFirst, SepLast));
        Assert.Equal(2000m, housing.AmountFor(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)));
    }

    [Fact]
    public void Starting_mid_month_pays_the_days_it_covers()
    {
        var transport = new SalaryAllowance(Employee, "بدل مواصلات", 600m, new DateOnly(2026, 9, 21), null);
        Assert.Equal(200m, transport.AmountFor(SepFirst, SepLast)); // 10 of 30 days
    }

    [Fact]
    public void Nothing_is_paid_outside_its_dates()
    {
        var ended = new SalaryAllowance(Employee, "بدل", 900m, new DateOnly(2026, 1, 1), new DateOnly(2026, 8, 31));
        var future = new SalaryAllowance(Employee, "بدل", 900m, new DateOnly(2026, 10, 1), null);
        Assert.Equal(0m, ended.AmountFor(SepFirst, SepLast));
        Assert.Equal(0m, future.AmountFor(SepFirst, SepLast));
    }

    [Fact]
    public void Ending_it_stops_payment_after_the_last_day()
    {
        var a = new SalaryAllowance(Employee, "بدل", 3000m, new DateOnly(2026, 1, 1), null);
        a.EndOn(new DateOnly(2026, 9, 10));
        Assert.Equal(1000m, a.AmountFor(SepFirst, SepLast));
        Assert.Throws<DomainException>(() => a.EndOn(new DateOnly(2025, 12, 31)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public void The_amount_must_be_positive(decimal amount) =>
        Assert.Throws<DomainException>(() => new SalaryAllowance(Employee, "بدل", amount, SepFirst, null));
}
