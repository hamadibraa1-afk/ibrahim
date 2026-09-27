using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FieldAttendance.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldAttendance.Api.Tests;

/// <summary>Bonuses and overtime are paid when HR decides to pay them, at the amount fixed then.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class ExtraPaymentTests(ApiFactory api)
{
    private const string Route = "/api/hr/payroll/extra-payments";

    [Fact]
    public async Task Recorded_overtime_is_shown_but_not_paid_on_its_own()
    {
        var hr = await api.ClientForAsync("1005");
        var anyone = await OfficeEmployeeAsync(skip: 5);
        await SalaryInEffectTests.PayslipAsync(hr, 2026, 9, anyone.UserId);

        var cycle = await CycleIdAsync(hr, 2026, 9);
        var slips = (await hr.GetFromJsonAsync<JsonElement[]>($"/api/hr/payroll/cycles/{cycle}/payslips"))!;
        var withOvertime = slips.Where(s => s.GetProperty("overtimeMinutes").GetInt32() > 0).ToList();

        Assert.NotEmpty(withOvertime);
        Assert.All(withOvertime, s => Assert.DoesNotContain(s.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("label").GetString()!.Contains("إضافي", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Hr_pays_overtime_by_hours_and_a_bonus_and_the_amounts_hold_after_a_raise()
    {
        var hr = await api.ClientForAsync("1005");
        var employee = await OfficeEmployeeAsync(skip: 4);

        var overtime = await hr.PostAsJsonAsync(Route, new { employeeId = employee.UserId, year = 2026, month = 9, kind = "Overtime", hours = 10m, reason = "حملة التبرعات" });
        var bonus = await hr.PostAsJsonAsync(Route, new { employeeId = employee.UserId, year = 2026, month = 9, kind = "Bonus", amount = 750m, reason = "تميز" });
        Assert.True(overtime.IsSuccessStatusCode, await overtime.Content.ReadAsStringAsync());
        Assert.True(bonus.IsSuccessStatusCode, await bonus.Content.ReadAsStringAsync());

        // Default policy: salary / 30 days / 8 hours × 1.25, for 10 hours.
        var expected = Math.Round(employee.BasicSalary / 30m / 8m * 1.25m * 10m, 2, MidpointRounding.AwayFromZero);
        var paid = (await overtime.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("amount").GetDecimal();
        Assert.Equal(expected, paid);

        // A raise for the same month changes the basic, not a payment already decided.
        var raise = await hr.PostAsJsonAsync($"/api/hr/employees/{employee.Id}/raise",
            new { kind = "Percent", value = 20m, effectiveFrom = new DateOnly(2026, 9, 1), reason = "تعديل" });
        Assert.True(raise.IsSuccessStatusCode, await raise.Content.ReadAsStringAsync());

        var slip = await SalaryInEffectTests.PayslipAsync(hr, 2026, 9, employee.UserId);
        var items = slip.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("amount").GetDecimal() == paid && !i.GetProperty("isDeduction").GetBoolean()
                                    && i.GetProperty("label").GetString()!.Contains("إضافي", StringComparison.Ordinal));
        Assert.Contains(items, i => i.GetProperty("amount").GetDecimal() == 750m && i.GetProperty("label").GetString()!.Contains("مكافأة", StringComparison.Ordinal));

        var listed = (await hr.GetFromJsonAsync<JsonElement[]>($"{Route}?year=2026&month=9"))!;
        Assert.Equal(2, listed.Count(p => p.GetProperty("employeeId").GetGuid() == employee.UserId));
    }

    [Fact]
    public async Task No_payment_is_added_to_or_removed_from_an_approved_month()
    {
        var hr = await api.ClientForAsync("1005");
        var employee = await OfficeEmployeeAsync(skip: 4);
        var early = await hr.PostAsJsonAsync(Route, new { employeeId = employee.UserId, year = 2025, month = 11, kind = "Bonus", amount = 100m, reason = "قبل الاعتماد" });
        Assert.True(early.IsSuccessStatusCode, await early.Content.ReadAsStringAsync());
        var earlyId = (await early.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await SalaryAllowanceTests.ApproveMonthAsync(hr, 2025, 11);

        var added = await hr.PostAsJsonAsync(Route, new { employeeId = employee.UserId, year = 2025, month = 11, kind = "Bonus", amount = 100m, reason = "متأخر" });
        var removed = await hr.DeleteAsync($"{Route}/{earlyId}");

        foreach (var response in new[] { added, removed })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("payroll.period_locked", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task An_hr_officer_cannot_pay_anyone()
    {
        var officer = await api.ClientForAsync("1006");
        var employee = await OfficeEmployeeAsync(skip: 4);

        var response = await officer.PostAsJsonAsync(Route, new { employeeId = employee.UserId, year = 2026, month = 9, kind = "Bonus", amount = 100m, reason = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<Guid> CycleIdAsync(HttpClient hr, int year, int month) =>
        (await hr.GetFromJsonAsync<JsonElement[]>("/api/hr/payroll/cycles"))!
            .Single(c => c.GetProperty("year").GetInt32() == year && c.GetProperty("month").GetInt32() == month).GetProperty("id").GetGuid();

    private Task<(Guid Id, Guid UserId, decimal BasicSalary)> OfficeEmployeeAsync(int skip) =>
        api.QueryAsync(async db =>
        {
            var p = await (from profile in db.EmployeeProfiles join u in db.Users on profile.UserId equals u.Id
                           where u.Role == UserRole.Employee && profile.Workforce == Workforce.Office
                           orderby u.EmployeeNumber descending
                           select new { profile.Id, profile.UserId, profile.BasicSalary }).Skip(skip).FirstAsync();
            return (p.Id, p.UserId, p.BasicSalary);
        });
}
