using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FieldAttendance.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldAttendance.Api.Tests;

/// <summary>Fixed monthly allowances: their own payslip lines, never part of the basic.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class SalaryAllowanceTests(ApiFactory api)
{
    [Fact]
    public async Task A_monthly_allowance_is_its_own_payslip_line_and_leaves_the_basic_alone()
    {
        var hr = await api.ClientForAsync("1005");
        var employee = await OfficeEmployeeAsync(skip: 2);

        var added = await hr.PostAsJsonAsync($"/api/hr/employees/{employee.Id}/allowances",
            new { name = "بدل سكن", monthlyAmount = 1500m, fromDate = new DateOnly(2026, 9, 1), toDate = (DateOnly?)null });
        Assert.True(added.IsSuccessStatusCode, await added.Content.ReadAsStringAsync());

        var list = (await hr.GetFromJsonAsync<JsonElement[]>($"/api/hr/employees/{employee.Id}/allowances"))!;
        Assert.Contains(list, a => a.GetProperty("name").GetString() == "بدل سكن" && a.GetProperty("monthlyAmount").GetDecimal() == 1500m);

        var slip = await SalaryInEffectTests.PayslipAsync(hr, 2026, 9, employee.UserId);
        Assert.Equal(employee.BasicSalary, slip.GetProperty("basicSalary").GetDecimal());
        Assert.Contains(slip.GetProperty("items").EnumerateArray(), i =>
            i.GetProperty("label").GetString() == "بدل سكن" && i.GetProperty("amount").GetDecimal() == 1500m
            && !i.GetProperty("isDeduction").GetBoolean());
    }

    [Fact]
    public async Task Allowances_are_salary_data_so_an_hr_officer_cannot_see_them()
    {
        var officer = await api.ClientForAsync("1006");
        var employee = await OfficeEmployeeAsync(skip: 2);

        var response = await officer.GetAsync($"/api/hr/employees/{employee.Id}/allowances");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Nothing_about_pay_can_be_dated_into_an_approved_month()
    {
        var hr = await api.ClientForAsync("1005");
        var employee = await OfficeEmployeeAsync(skip: 3);
        await ApproveMonthAsync(hr, 2025, 12);

        var allowance = await hr.PostAsJsonAsync($"/api/hr/employees/{employee.Id}/allowances",
            new { name = "بدل مواصلات", monthlyAmount = 500m, fromDate = new DateOnly(2025, 12, 15), toDate = (DateOnly?)null });
        var salary = await hr.PostAsJsonAsync($"/api/hr/employees/{employee.Id}/salary",
            new { newSalary = employee.BasicSalary + 500m, effectiveFrom = new DateOnly(2025, 12, 20), reason = "تصحيح" });
        var raise = await hr.PostAsJsonAsync($"/api/hr/employees/{employee.Id}/raise",
            new { kind = "Amount", value = 500m, effectiveFrom = new DateOnly(2025, 12, 20), reason = "تصحيح" });

        foreach (var response in new[] { allowance, salary, raise })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("payroll.period_locked", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    internal static async Task ApproveMonthAsync(HttpClient hr, int year, int month)
    {
        var opened = await hr.PostAsJsonAsync("/api/hr/payroll/cycles", new { year, month });
        Assert.True(opened.IsSuccessStatusCode, await opened.Content.ReadAsStringAsync());
        var id = (await opened.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.True((await hr.PostAsync($"/api/hr/payroll/cycles/{id}/calculate", null)).IsSuccessStatusCode);
        var approved = await hr.PostAsync($"/api/hr/payroll/cycles/{id}/approve", null);
        Assert.True(approved.IsSuccessStatusCode, await approved.Content.ReadAsStringAsync());
    }

    /// <summary>Office employees counted from the highest number down; other salary tests use the first two.</summary>
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
