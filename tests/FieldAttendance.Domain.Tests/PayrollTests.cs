using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Payroll;
using Xunit;
using static FieldAttendance.Domain.Tests.TestTime;

namespace FieldAttendance.Domain.Tests;

public class PayrollMathTests
{
    private static readonly PayrollPolicy Policy = new(30, 480, 1.25m, 25m);

    [Fact]
    public void DailyValue_IsSalaryOverMonthDays()
    {
        Assert.Equal(166.67m, PayrollMath.DailyValue(5000m, Policy));
        Assert.Equal(66.67m, PayrollMath.DailyValue(2000m, Policy));
    }

    [Fact]
    public void HourlyValue_DividesTheDayByContractedHours()
    {
        Assert.Equal(20.83m, PayrollMath.HourlyValue(5000m, Policy));
        Assert.Equal(8.33m, PayrollMath.HourlyValue(2000m, Policy));
    }

    [Fact]
    public void ADayOff_CostsEachEmployeeADifferentAmount()
    {
        var rich = PayrollMath.DeductionAmount(5000m, Policy, DeductionUnit.Day, 1);
        var modest = PayrollMath.DeductionAmount(2000m, Policy, DeductionUnit.Day, 1);
        Assert.Equal(166.67m, rich);
        Assert.Equal(66.67m, modest);
        Assert.True(rich > modest);
    }

    [Fact]
    public void TwoHours_AreProportionalToo()
    {
        Assert.Equal(41.67m, PayrollMath.DeductionAmount(5000m, Policy, DeductionUnit.Hour, 2));
        Assert.Equal(16.67m, PayrollMath.DeductionAmount(2000m, Policy, DeductionUnit.Hour, 2));
    }

    [Fact]
    public void HalfADay_IsHalfTheDailyValue() =>
        Assert.Equal(83.33m, PayrollMath.DeductionAmount(5000m, Policy, DeductionUnit.Day, 0.5m));

    [Fact]
    public void FixedAmount_IgnoresTheSalary() =>
        Assert.Equal(150m, PayrollMath.DeductionAmount(5000m, Policy, DeductionUnit.FixedAmount, 150m));

    [Fact]
    public void Overtime_UsesTheHourlyRateAndTheFactor() =>
        Assert.Equal(52.08m, PayrollMath.OvertimeAmount(5000m, Policy, 120));

    [Fact]
    public void Money_IsComputedFromTheExactRate_NotTheRoundedOne()
    {
        // 20.8333… × 2 = 41.67, while rounding the rate first would lose a fils and give 41.66.
        Assert.Equal(41.67m, PayrollMath.DeductionAmount(5000m, Policy, DeductionUnit.Hour, 2));
        Assert.Equal(20.83m, PayrollMath.HourlyValue(5000m, Policy));
    }

    [Fact]
    public void Overtime_IsZeroWhenThereIsNone() =>
        Assert.Equal(0m, PayrollMath.OvertimeAmount(5000m, Policy, 0));

    [Fact]
    public void Deductions_AreCappedByPolicy()
    {
        // 25% of 5,000 is 1,250 — a larger request is trimmed to the ceiling.
        Assert.Equal(1250m, PayrollMath.CapDeductions(5000m, Policy, 2000m));
        Assert.Equal(400m, PayrollMath.CapDeductions(5000m, Policy, 400m));
    }

    [Fact]
    public void NetPay_NeverGoesNegative() => Assert.Equal(0m, PayrollMath.Net(1000m, 1500m));

    [Fact]
    public void ImpossiblePolicy_IsRejected() =>
        Assert.Throws<DomainException>(() => PayrollMath.DailyValue(5000m, new PayrollPolicy(0, 480, 1m, 25m)));
}

public class DeductionWorkflowTests
{
    private static readonly Guid Employee = Guid.NewGuid();
    private static readonly Guid Supervisor = Guid.NewGuid();
    private static readonly Guid Type = Guid.NewGuid();

    private static DeductionProposal Proposal() =>
        new(Employee, Type, new DateOnly(2026, 9, 20), 1m, "غياب بدون إذن", null, Supervisor);

    [Fact]
    public void ANewProposal_CarriesNoMoneyYet()
    {
        var p = Proposal();
        Assert.Equal(DeductionStatus.Proposed, p.Status);
        Assert.Null(p.ApprovedAmount);
    }

    [Fact]
    public void Approval_FreezesTheAmount()
    {
        var p = Proposal();
        p.Approve(Supervisor, T(9, 0), 166.67m, null, "اعتماد");
        Assert.Equal(DeductionStatus.Approved, p.Status);
        Assert.Equal(166.67m, p.ApprovedAmount);
    }

    [Fact]
    public void Approval_CanReduceTheUnits()
    {
        var p = Proposal();
        p.Approve(Supervisor, T(9, 0), 83.34m, 0.5m, "تخفيض لنصف يوم");
        Assert.Equal(0.5m, p.Units);
        Assert.Equal(83.34m, p.ApprovedAmount);
    }

    [Fact]
    public void AWarning_ReplacesTheMoney()
    {
        var p = Proposal();
        p.ConvertToWarning(Supervisor, T(9, 0), Guid.NewGuid(), "إنذار أول بدل الخصم");
        Assert.Equal(DeductionStatus.ConvertedToWarning, p.Status);
        Assert.Null(p.ApprovedAmount);
    }

    [Fact]
    public void ADecidedProposal_CannotBeDecidedAgain()
    {
        var p = Proposal();
        p.Cancel(Supervisor, T(9, 0), "خطأ في الرصد");
        Assert.Throws<DomainException>(() => p.Approve(Supervisor, T(9, 5), 100m, null, null));
    }

    [Fact]
    public void Cancelling_RequiresAReason() =>
        Assert.Throws<DomainException>(() => Proposal().Cancel(Supervisor, T(9, 0), " "));

    [Fact]
    public void OnlyAnApprovedDeduction_EntersPayroll()
    {
        var p = Proposal();
        Assert.Throws<DomainException>(() => p.AttachToPayroll(Guid.NewGuid()));
        p.Approve(Supervisor, T(9, 0), 166.67m, null, null);
        p.AttachToPayroll(Guid.NewGuid());
        Assert.NotNull(p.PayrollCycleId);
    }

    [Fact]
    public void AWarning_CanBeObjectedToOnce()
    {
        var w = new Warning(Employee, Guid.NewGuid(), "تكرار التأخير", Supervisor, T(9, 0), new DateOnly(2026, 9, 20));
        w.FileObjection("كنت في مهمة رسمية", T(10, 0));
        Assert.NotNull(w.AcknowledgedAt);
        Assert.Throws<DomainException>(() => w.FileObjection("مرة أخرى", T(11, 0)));
    }

    [Fact]
    public void AWarning_StopsCountingAfterItsValidity()
    {
        var w = new Warning(Employee, Guid.NewGuid(), "تأخير", Supervisor, T(9, 0), null);
        Assert.True(w.IsInForce(new DateOnly(2026, 10, 1), 180));
        Assert.False(w.IsInForce(new DateOnly(2027, 10, 1), 180));
    }
}

public class PayrollCycleTests
{
    private static readonly Guid User = Guid.NewGuid();

    private static PayrollCycle Cycle() => new(2026, 9, 30, 480, 1.25m, 25m, User);

    [Fact]
    public void ACycle_CannotBeApprovedBeforeItIsCalculated()
    {
        var c = Cycle();
        Assert.Throws<DomainException>(() => c.Approve(User, T(9, 0)));
    }

    [Fact]
    public void TheHappyPath_RunsDraftToClosed()
    {
        var c = Cycle();
        c.MarkCalculated(T(9, 0));
        c.SendToReview();
        c.Approve(User, T(10, 0));
        c.Close(T(11, 0));
        Assert.Equal(PayrollStatus.Closed, c.Status);
        Assert.False(c.IsOpen);
    }

    [Fact]
    public void AnApprovedCycle_CannotBeRecalculated()
    {
        var c = Cycle();
        c.MarkCalculated(T(9, 0));
        c.Approve(User, T(10, 0));
        Assert.Throws<DomainException>(c.EnsureOpen);
    }

    [Fact]
    public void ClosingBeforeApproval_IsRefused() => Assert.Throws<DomainException>(() => Cycle().Close(T(9, 0)));

    [Fact]
    public void ReopeningClearsTheApproval()
    {
        var c = Cycle();
        c.MarkCalculated(T(9, 0));
        c.Approve(User, T(10, 0));
        c.Close(T(11, 0));
        c.Reopen();
        Assert.True(c.IsOpen);
        Assert.Null(c.ApprovedAt);
    }

    [Fact]
    public void APayslip_TotalsItsLinesAndRespectsTheCap()
    {
        var line = new PayrollLine(Guid.NewGuid(), Guid.NewGuid(), 5000m);
        line.SetAttendance(22, 20, 2, 0, 0, 45, 120);
        line.AddItem("الراتب الأساسي", 5000m, isDeduction: false);
        line.AddItem("بدل مواصلات", 300m, isDeduction: false);
        line.AddItem("غياب يومين", 333.34m, isDeduction: true);
        line.Total(cappedDeductions: 333.34m);

        Assert.Equal(5300m, line.Earnings);
        Assert.Equal(333.34m, line.Deductions);
        Assert.Equal(4966.66m, line.NetPay);
    }

    [Fact]
    public void ARecalculation_ReplacesTheOldLinesInsteadOfAddingToThem()
    {
        var line = new PayrollLine(Guid.NewGuid(), Guid.NewGuid(), 4000m);
        line.AddItem("الراتب الأساسي", 4000m, false);
        line.ClearItems();
        line.AddItem("الراتب الأساسي", 4000m, false);
        line.Total(0m);
        Assert.Single(line.Items);
        Assert.Equal(4000m, line.NetPay);
    }
}
