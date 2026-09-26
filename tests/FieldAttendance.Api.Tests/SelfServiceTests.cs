using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FieldAttendance.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldAttendance.Api.Tests;

/// <summary>
/// Self-service under /api/me belongs to anyone the organisation keeps attendance for, whatever
/// their role. Accounts from the demo data: 1005 is the HR manager (office profile, office
/// schedule), 2001 a field collector (site assignment, no HR profile), 1001 the system
/// administrator (neither).
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class SelfServiceTests(ApiFactory api)
{
    private const string HrManager = "1005";
    private const string Collector = "2001";
    private const string AdminWithoutProfile = "1001";

    private static readonly DateOnly Today = DateOnly.FromDateTime(ApiFactory.Now.ToOffset(TimeSpan.FromHours(4)).DateTime);

    [Fact]
    public async Task An_hr_manager_can_open_today_and_sees_their_own_office_shift()
    {
        var client = await api.ClientForAsync(HrManager);

        var response = await client.GetAsync("/api/me/today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var shifts = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.Contains(shifts!, s => s.GetProperty("shiftDate").GetString() == Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task An_hr_manager_can_submit_a_leave_request_and_follow_it()
    {
        var client = await api.ClientForAsync(HrManager);
        var id = await SubmitLeaveAsync(client, new DateOnly(2026, 10, 12));

        var mine = await client.GetFromJsonAsync<JsonElement[]>("/api/me/leaves");
        Assert.Contains(mine!, l => l.GetProperty("id").GetGuid() == id);

        var timeline = await client.GetAsync($"/api/me/leaves/{id}/timeline");
        Assert.Equal(HttpStatusCode.OK, timeline.StatusCode);
    }

    [Fact]
    public async Task A_managers_own_leave_request_never_routes_to_themselves()
    {
        var client = await api.ClientForAsync(HrManager);
        var managerId = await api.UserIdAsync(HrManager);
        var id = await SubmitLeaveAsync(client, new DateOnly(2026, 10, 19));

        var approvers = await api.QueryAsync(db => db.ApprovalSteps.AsNoTracking()
            .Where(s => s.Kind == RequestKind.Leave && s.RequestId == id).Select(s => s.ApproverId).ToListAsync());

        Assert.NotEmpty(approvers);
        Assert.DoesNotContain(managerId, approvers.OfType<Guid>());
    }

    [Theory]
    [InlineData("/api/me/today")]
    [InlineData("/api/me/schedule")]
    [InlineData("/api/me/permissions")]
    [InlineData("/api/me/leaves")]
    [InlineData("/api/me/leaves/balances")]
    public async Task An_account_without_an_employment_record_gets_empty_screens_not_an_error(string path)
    {
        var client = await api.ClientForAsync(AdminWithoutProfile);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<JsonElement[]>())!);
    }

    [Fact]
    public async Task An_account_without_an_employment_record_cannot_file_leave()
    {
        var client = await api.ClientForAsync(AdminWithoutProfile);

        var response = await client.PostAsJsonAsync("/api/me/leaves", await LeaveBodyAsync(new DateOnly(2026, 11, 2)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("self.no_employment_record", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task A_collector_without_an_hr_profile_keeps_self_service()
    {
        var client = await api.ClientForAsync(Collector);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me/today")).StatusCode);
        Assert.NotEmpty((await client.GetFromJsonAsync<JsonElement[]>("/api/me/leaves/balances"))!);
    }

    [Fact]
    public async Task Geofencing_rejects_an_hr_manager_checking_in_from_home_like_anyone_else()
    {
        var client = await api.ClientForAsync(HrManager);

        // Dubai Marina: about 55 km from the Sharjah head office.
        var response = await client.PostAsJsonAsync("/api/me/check-in", new { latitude = 25.0805, longitude = 55.1403, accuracy = 10 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("geofence.outside", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Nobody_can_read_the_approval_timeline_of_someone_elses_leave()
    {
        var owner = await api.ClientForAsync(HrManager);
        var id = await SubmitLeaveAsync(owner, new DateOnly(2026, 10, 26));
        var other = await api.ClientForAsync(Collector);

        var response = await other.GetAsync($"/api/me/leaves/{id}/timeline");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("request.not_found", await ErrorCodeAsync(response));
    }

    private async Task<Guid> SubmitLeaveAsync(HttpClient client, DateOnly from)
    {
        var response = await client.PostAsJsonAsync("/api/me/leaves", await LeaveBodyAsync(from));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        var mine = await client.GetFromJsonAsync<JsonElement[]>("/api/me/leaves");
        return mine!.Single(l => l.GetProperty("fromDate").GetString() == from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).GetProperty("id").GetGuid();
    }

    private async Task<object> LeaveBodyAsync(DateOnly from)
    {
        var annual = await api.QueryAsync(db => db.LeaveTypes.Where(t => t.NameEn == "Annual leave").Select(t => t.Id).SingleAsync());
        return new { leaveTypeId = annual, fromDate = from, toDate = from.AddDays(1), reason = "test" };
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
}
