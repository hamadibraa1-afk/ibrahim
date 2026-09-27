using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Payroll;
using Xunit;

namespace FieldAttendance.Domain.Tests;

public class SalaryRaiseTests
{
    [Theory]
    [InlineData(RaiseKind.Amount, 750, 5750)]
    [InlineData(RaiseKind.Percent, 10, 5500)]
    [InlineData(RaiseKind.Percent, 2.5, 5125)]
    public void A_raise_is_added_to_the_basic(RaiseKind kind, decimal value, decimal expected) =>
        Assert.Equal(expected, SalaryRaise.Apply(5000m, kind, value));

    [Fact]
    public void A_percentage_raise_is_rounded_to_fils_once() =>
        Assert.Equal(4463.33m, SalaryRaise.Apply(4333.33m, RaiseKind.Percent, 3m));

    [Theory]
    [InlineData(RaiseKind.Amount, 0)]
    [InlineData(RaiseKind.Amount, -100)]
    [InlineData(RaiseKind.Percent, 0)]
    [InlineData(RaiseKind.Percent, 101)]
    public void A_raise_must_be_an_increase_within_reason(RaiseKind kind, decimal value) =>
        Assert.Throws<DomainException>(() => SalaryRaise.Apply(5000m, kind, value));
}
