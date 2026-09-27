using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldAttendance.Api.Tests;

/// <summary>
/// Head office runs a 08:00–16:00 day with 30 minutes of flexible hours in the demo data, and the
/// suite's clock is Tuesday 09:30 in Dubai. 1006 is the HR officer (head office, office schedule),
/// kept out of every other test so their check-in cannot disturb one.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class FlexAndComplianceTests(ApiFactory api)
{
    private static readonly DateTimeOffset LeaveAt = new(2026, 9, 22, 16, 30, 0, TimeSpan.FromHours(4));

    [Fact]
    public async Task After_checking_in_the_employee_is_told_their_own_leaving_time()
    {
        var client = await api.ClientForAsync("1006");
        var before = (await client.GetFromJsonAsync<JsonElement[]>("/api/me/today"))!.Single();
        Assert.Equal(30, before.GetProperty("flexMinutes").GetInt32());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("expectedCheckOutAt").ValueKind);

        var checkIn = await client.PostAsJsonAsync("/api/me/check-in", new
        {
            latitude = before.GetProperty("locationLatitude").GetDouble(),
            longitude = before.GetProperty("locationLongitude").GetDouble(),
            accuracy = 10,
        });
        Assert.Equal(HttpStatusCode.NoContent, checkIn.StatusCode);

        // In at 09:30, past the 08:30 end of flex: the day is 08:30–16:30 and an hour is late.
        var after = (await client.GetFromJsonAsync<JsonElement[]>("/api/me/today"))!.Single();
        Assert.Equal(LeaveAt, after.GetProperty("expectedCheckOutAt").GetDateTimeOffset());
        Assert.Equal(60, after.GetProperty("lateUnexcused").GetInt32());
    }

    [Fact]
    public async Task An_employee_sees_their_month_to_date_compliance()
    {
        var client = await api.ClientForAsync("1005");

        var figure = await client.GetFromJsonAsync<JsonElement>("/api/me/compliance");

        Assert.True(figure.GetProperty("countedDays").GetInt32() > 0);
        var percent = figure.GetProperty("percent").GetDecimal();
        Assert.InRange(percent, 0m, 100m);
        Assert.Equal(9, figure.GetProperty("month").GetInt32());
    }

    [Fact]
    public async Task An_account_with_no_attendance_has_no_figure_rather_than_an_error()
    {
        var client = await api.ClientForAsync(ApiFactory.LegacyAccount);

        var response = await client.GetAsync("/api/me/compliance");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var figure = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, figure.GetProperty("percent").ValueKind);
        Assert.Equal(0, figure.GetProperty("countedDays").GetInt32());
    }

    [Fact]
    public async Task Hr_sees_an_employees_compliance_and_it_matches_what_the_employee_sees()
    {
        var profileId = await ProfileIdAsync("3001");
        var hr = await api.ClientForAsync("1005");
        var self = await api.ClientForAsync("3001");

        var asHr = await hr.GetFromJsonAsync<JsonElement>($"/api/hr/employees/{profileId}/compliance");
        var asSelf = await self.GetFromJsonAsync<JsonElement>("/api/me/compliance");

        Assert.Equal(asSelf.GetProperty("percent").GetDecimal(), asHr.GetProperty("percent").GetDecimal());
    }

    [Fact]
    public async Task A_collector_cannot_read_anyone_elses_compliance()
    {
        var profileId = await ProfileIdAsync("3001");
        var collector = await api.ClientForAsync("2001");

        var response = await collector.GetAsync($"/api/hr/employees/{profileId}/compliance");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_department_manager_cannot_read_compliance_outside_their_department()
    {
        var managerId = await api.UserIdAsync("3006");
        var outsider = await api.QueryAsync(db => (
            from p in db.EmployeeProfiles
            join d in db.Departments on p.DepartmentId equals d.Id
            where d.ManagerId != managerId && p.ManagerId != managerId && p.UserId != managerId
            select p.Id).FirstAsync());
        var manager = await api.ClientForAsync("3006");

        var response = await manager.GetAsync($"/api/hr/employees/{outsider}/compliance");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("access.forbidden", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    private Task<Guid> ProfileIdAsync(string employeeNumber) =>
        api.QueryAsync(db => (
            from p in db.EmployeeProfiles
            join u in db.Users on p.UserId equals u.Id
            where u.EmployeeNumber == employeeNumber
            select p.Id).SingleAsync());
}
