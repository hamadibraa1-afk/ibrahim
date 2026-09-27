using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldAttendance.Api.Tests;

/// <summary>Whether a leave is paid is a setting of its type, chosen by HR, not guessed from its name.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class LeaveTypeTests(ApiFactory api)
{
    [Fact]
    public async Task An_unpaid_type_is_deducted_whatever_it_is_called()
    {
        var hr = await api.ClientForAsync("1005");
        var created = await hr.PostAsJsonAsync("/api/hr/leave-types",
            new { nameAr = "إجازة رعاية أسرية", nameEn = "Family care leave", annualBalanceDays = (int?)null, isPaid = false, requiresAttachment = false });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var typeId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var employee = await api.QueryAsync(db => (
            from p in db.EmployeeProfiles join u in db.Users on p.UserId equals u.Id
            where u.Role == UserRole.Employee && p.Workforce == Workforce.Office
            orderby u.EmployeeNumber descending
            select p.UserId).Skip(6).FirstAsync());
        await api.QueryAsync(async db =>
        {
            var leave = new LeaveRequest(employee, typeId, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6), 2, "رعاية", employee);
            leave.Approve(employee, ApiFactory.Now);
            db.LeaveRequests.Add(leave);
            return await db.SaveChangesAsync();
        });

        var slip = await SalaryInEffectTests.PayslipAsync(hr, 2026, 10, employee);

        Assert.Equal(2, slip.GetProperty("unpaidLeaveDays").GetInt32());
        Assert.Contains(slip.GetProperty("items").EnumerateArray(), i => i.GetProperty("isDeduction").GetBoolean()
            && i.GetProperty("label").GetString() == "إجازة بدون راتب");
    }

    [Fact]
    public async Task Everyone_sees_whether_a_type_is_paid_and_needs_a_document()
    {
        var employee = await api.ClientForAsync("1006");
        var types = (await employee.GetFromJsonAsync<JsonElement[]>("/api/leave-types"))!;

        var sick = types.Single(t => t.GetProperty("nameEn").GetString() == "Sick leave");
        var unpaid = types.Single(t => t.GetProperty("nameEn").GetString() == "Unpaid leave");
        Assert.True(sick.GetProperty("isPaid").GetBoolean());
        Assert.True(sick.GetProperty("requiresAttachment").GetBoolean());
        Assert.False(unpaid.GetProperty("isPaid").GetBoolean());
    }

    [Fact]
    public async Task Only_hr_management_defines_leave_types()
    {
        var officer = await api.ClientForAsync("1006");

        var response = await officer.PostAsJsonAsync("/api/hr/leave-types",
            new { nameAr = "س", nameEn = "x", annualBalanceDays = 1, isPaid = true, requiresAttachment = false });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
