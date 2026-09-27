using System.Net.Http.Json;
using System.Text.Json;
using FieldAttendance.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldAttendance.Api.Tests;

/// <summary>A salary change is paid from its effective date, not from the day HR typed it in.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class SalaryInEffectTests(ApiFactory api)
{
    [Fact]
    public async Task A_raise_dated_next_month_is_not_paid_this_month()
    {
        var hr = await api.ClientForAsync("1005");
        // The highest-numbered office employee: no other test touches their pay.
        var employee = await api.QueryAsync(db => (
            from p in db.EmployeeProfiles join u in db.Users on p.UserId equals u.Id
            where u.Role == UserRole.Employee && p.Workforce == Workforce.Office
            orderby u.EmployeeNumber descending
            select new { p.Id, p.UserId, p.BasicSalary }).FirstAsync());

        var raise = await hr.PostAsJsonAsync($"/api/hr/employees/{employee.Id}/salary",
            new { newSalary = employee.BasicSalary + 1000m, effectiveFrom = new DateOnly(2026, 10, 1), reason = "علاوة سنوية" });
        Assert.True(raise.IsSuccessStatusCode, await raise.Content.ReadAsStringAsync());

        // Today (22 September) the employee is still on the old figure.
        var detail = await hr.GetFromJsonAsync<JsonElement>($"/api/hr/employees/{employee.Id}");
        Assert.Equal(employee.BasicSalary, detail.GetProperty("basicSalary").GetDecimal());

        var september = await PayslipAsync(hr, 2026, 9, employee.UserId);
        var october = await PayslipAsync(hr, 2026, 10, employee.UserId);
        Assert.Equal(employee.BasicSalary, september.GetProperty("basicSalary").GetDecimal());
        Assert.Equal(employee.BasicSalary + 1000m, october.GetProperty("basicSalary").GetDecimal());
    }

    private static async Task<JsonElement> PayslipAsync(HttpClient hr, int year, int month, Guid employeeId)
    {
        var cycles = (await hr.GetFromJsonAsync<JsonElement[]>("/api/hr/payroll/cycles"))!;
        var cycle = cycles.FirstOrDefault(c => c.GetProperty("year").GetInt32() == year && c.GetProperty("month").GetInt32() == month);
        if (cycle.ValueKind == JsonValueKind.Undefined)
        {
            var opened = await hr.PostAsJsonAsync("/api/hr/payroll/cycles", new { year, month });
            Assert.True(opened.IsSuccessStatusCode, await opened.Content.ReadAsStringAsync());
            cycle = await opened.Content.ReadFromJsonAsync<JsonElement>();
        }
        var id = cycle.GetProperty("id").GetGuid();

        var calculated = await hr.PostAsync($"/api/hr/payroll/cycles/{id}/calculate", null);
        Assert.True(calculated.IsSuccessStatusCode, await calculated.Content.ReadAsStringAsync());
        var slips = (await hr.GetFromJsonAsync<JsonElement[]>($"/api/hr/payroll/cycles/{id}/payslips"))!;
        return slips.Single(s => s.GetProperty("employeeId").GetGuid() == employeeId);
    }
}
