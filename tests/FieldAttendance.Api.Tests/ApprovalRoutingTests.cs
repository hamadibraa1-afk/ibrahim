using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FieldAttendance.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldAttendance.Api.Tests;

/// <summary>
/// Field requests go supervisor, then the supervisor's department manager. In the demo data the
/// collectors of the city sites report to 1002, and the supervisors to 3008, who manages Fundraising.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class ApprovalRoutingTests(ApiFactory api)
{
    private static readonly DateOnly Wednesday = new(2026, 9, 23);

    [Fact]
    public async Task A_collectors_permission_goes_to_the_supervisor_then_the_supervisors_department_manager()
    {
        var id = await SubmitLateAsync("2001");
        var supervisor = await api.ClientForAsync("1002");
        var manager = await api.ClientForAsync("3008");

        Assert.Contains(id, await PendingAsync(supervisor));
        Assert.DoesNotContain(id, await PendingAsync(manager));

        Assert.Equal(HttpStatusCode.NoContent, (await supervisor.PostAsJsonAsync($"/api/requests/permissions/{id}/approve", new { })).StatusCode);
        Assert.Equal(RequestStatus.Pending, await StatusAsync(id));
        Assert.Contains(id, await PendingAsync(manager));

        Assert.Equal(HttpStatusCode.NoContent, (await manager.PostAsJsonAsync($"/api/requests/permissions/{id}/approve", new { })).StatusCode);
        Assert.Equal(RequestStatus.Approved, await StatusAsync(id));
    }

    [Fact]
    public async Task Nobody_signs_a_step_that_is_not_theirs()
    {
        var id = await SubmitLateAsync("2003");
        var hr = await api.ClientForAsync("1005");

        var response = await hr.PostAsJsonAsync($"/api/requests/permissions/{id}/approve", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("approval.not_yours", await CodeAsync(response));
        Assert.Equal(RequestStatus.Pending, await StatusAsync(id));
    }

    /// <summary>
    /// The cycle is fixed: the administrator signs only a step that is theirs. Before, the
    /// administrator could sign the supervisor's step and then the department manager's, so a field
    /// request could be approved without ever reaching the office side.
    /// </summary>
    [Fact]
    public async Task The_system_administrator_cannot_sign_someone_elses_step()
    {
        var id = await SubmitLateAsync("2004");
        var admin = await api.ClientForAsync("1001");

        var response = await admin.PostAsJsonAsync($"/api/requests/permissions/{id}/approve", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("approval.not_yours", await CodeAsync(response));
        Assert.Contains(id, await PendingAsync(await api.ClientForAsync("1002")));
    }

    [Fact]
    public async Task An_office_request_never_reaches_the_field_supervisor()
    {
        var office = await OfficeEmployeeAsync();
        var id = await SubmitLateAsync(office.Number);
        var supervisor = await api.ClientForAsync("1002");
        var lineManager = await api.ClientForAsync(office.ManagerNumber);

        Assert.DoesNotContain(id, await PendingAsync(supervisor));
        Assert.Contains(id, await PendingAsync(lineManager));
    }

    [Fact]
    public async Task A_department_manager_signs_their_step_of_an_office_leave()
    {
        var office = await OfficeEmployeeAsync();
        var employee = await api.ClientForAsync(office.Number);
        var annual = await api.QueryAsync(db => db.LeaveTypes.Where(t => t.NameEn == "Annual leave").Select(t => t.Id).SingleAsync());
        var submit = await employee.PostAsJsonAsync("/api/me/leaves", new { leaveTypeId = annual, fromDate = "2026-11-09", toDate = "2026-11-10", reason = "test" });
        Assert.True(submit.IsSuccessStatusCode, await submit.Content.ReadAsStringAsync());
        var employeeId = await api.UserIdAsync(office.Number);
        var leaveId = await api.QueryAsync(db => db.LeaveRequests.Where(l => l.EmployeeId == employeeId && l.FromDate == new DateOnly(2026, 11, 9)).Select(l => l.Id).SingleAsync());
        var departmentManager = await api.QueryAsync(db => db.ApprovalSteps
            .Where(s => s.RequestId == leaveId && s.Status == ApprovalStepStatus.Pending).OrderBy(s => s.Order)
            .Join(db.Users, s => s.ApproverId, u => u.Id, (s, u) => u.EmployeeNumber).FirstAsync());

        // This used to be 403: approving a leave required the supervisor role, whoever's step it was.
        var response = await (await api.ClientForAsync(departmentManager)).PostAsJsonAsync($"/api/requests/leaves/{leaveId}/approve", new { });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task A_request_with_nobody_in_its_chain_goes_to_hr_rather_than_nowhere()
    {
        // 3001 manages their own department and has no line manager: the office permission chain
        // (line manager only) is empty for them.
        var id = await SubmitLateAsync("3001");
        var hrManager = await api.UserIdAsync("1005");

        var waitingOn = await api.QueryAsync(db => db.ApprovalSteps
            .Where(s => s.RequestId == id && s.Status == ApprovalStepStatus.Pending).Select(s => s.ApproverId).SingleAsync());

        Assert.Equal(hrManager, waitingOn);
    }

    private async Task<Guid> SubmitLateAsync(string employeeNumber)
    {
        var employeeId = await api.UserIdAsync(employeeNumber);
        var start = await api.QueryAsync(db => (
            from a in db.Assignments join t in db.ShiftTemplates on a.ShiftTemplateId equals t.Id
            where a.EmployeeId == employeeId && a.IsActive && a.EndDate == null
            orderby t.StartTime select t.StartTime).FirstAsync());
        var client = await api.ClientForAsync(employeeNumber);

        var response = await client.PostAsJsonAsync("/api/me/permissions", new
        {
            type = "Late", shiftDate = Wednesday, toTime = start.AddMinutes(45).ToString("HH:mm:ss", CultureInfo.InvariantCulture), reason = "test",
        });

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<List<Guid>> PendingAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement[]>("/api/requests/permissions?status=Pending"))!
            .Select(p => p.GetProperty("id").GetGuid()).ToList();

    private Task<RequestStatus> StatusAsync(Guid id) =>
        api.QueryAsync(db => db.PermissionRequests.Where(p => p.Id == id).Select(p => p.Status).SingleAsync());

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    /// <summary>An office employee with a line manager, picked from the demo data rather than hard-coded.</summary>
    private Task<(string Number, string ManagerNumber)> OfficeEmployeeAsync() =>
        api.QueryAsync(async db =>
        {
            var row = await (
                from p in db.EmployeeProfiles
                join u in db.Users on p.UserId equals u.Id
                join m in db.Users on p.ManagerId equals m.Id
                where p.Workforce == Workforce.Office && u.Role == UserRole.Employee
                orderby u.EmployeeNumber descending
                select new { u.EmployeeNumber, Manager = m.EmployeeNumber }).FirstAsync();
            return (row.EmployeeNumber, row.Manager);
        });
}
