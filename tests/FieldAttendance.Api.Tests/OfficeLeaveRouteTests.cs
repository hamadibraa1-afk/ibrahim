using System.Net.Http.Json;
using FieldAttendance.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldAttendance.Api.Tests;

/// <summary>Office leave starts with the employee's direct manager, then the department manager, then HR.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class OfficeLeaveRouteTests(ApiFactory api)
{
    [Fact]
    public async Task A_branch_employees_leave_goes_to_their_direct_manager_first()
    {
        // Branch staff report to their branch lead, who is not the department manager.
        var employee = await OfficeEmployeeAsync(e => e.ManagerId != e.DepartmentManagerId);
        var steps = await SubmitAndReadChainAsync(employee.Number, new DateOnly(2026, 11, 9));

        Assert.Equal(ApprovalStage.LineManager, steps[0].Stage);
        Assert.Equal(employee.ManagerId, steps[0].ApproverId);
        Assert.Equal(ApprovalStepStatus.Pending, steps[0].Status);
        Assert.Contains(steps, s => s.Stage == ApprovalStage.DepartmentManager && s.ApproverId == employee.DepartmentManagerId
                                    && s.Status == ApprovalStepStatus.Pending);
        Assert.Equal(ApprovalStage.Hr, steps[^1].Stage);
    }

    [Fact]
    public async Task When_the_direct_manager_is_the_department_manager_they_sign_once()
    {
        var employee = await OfficeEmployeeAsync(e => e.ManagerId == e.DepartmentManagerId);
        var steps = await SubmitAndReadChainAsync(employee.Number, new DateOnly(2026, 11, 16));

        Assert.Single(steps, s => s.ApproverId == employee.ManagerId && s.Status == ApprovalStepStatus.Pending);
        Assert.Equal(ApprovalStage.LineManager, steps[0].Stage);
        Assert.Equal(ApprovalStage.Hr, steps.Last(s => s.Status == ApprovalStepStatus.Pending).Stage);
    }

    private async Task<List<(ApprovalStage Stage, Guid? ApproverId, ApprovalStepStatus Status)>> SubmitAndReadChainAsync(string number, DateOnly from)
    {
        var client = await api.ClientForAsync(number);
        var annual = await api.QueryAsync(db => db.LeaveTypes.Where(t => t.NameEn == "Annual leave").Select(t => t.Id).SingleAsync());
        var response = await client.PostAsJsonAsync("/api/me/leaves", new { leaveTypeId = annual, fromDate = from, toDate = from.AddDays(1), reason = "سفر" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var id = (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();

        var rows = await api.QueryAsync(db => db.ApprovalSteps.Where(s => s.RequestId == id).OrderBy(s => s.Order)
            .Select(s => new { s.Stage, s.ApproverId, s.Status }).ToListAsync());
        return rows.Select(r => (r.Stage, r.ApproverId, r.Status)).ToList();
    }

    private sealed record Candidate(string Number, Guid? ManagerId, Guid? DepartmentManagerId);

    /// <summary>Lowest-numbered matching office employee, so the other tests' people (picked from the top) stay clear.</summary>
    private async Task<Candidate> OfficeEmployeeAsync(Func<Candidate, bool> match)
    {
        var all = await api.QueryAsync(db => (
            from p in db.EmployeeProfiles join u in db.Users on p.UserId equals u.Id
            join d in db.Departments on p.DepartmentId equals d.Id
            where u.Role == UserRole.Employee && p.Workforce == Workforce.Office && p.ManagerId != null
            orderby u.EmployeeNumber
            select new Candidate(u.EmployeeNumber, p.ManagerId, d.ManagerId)).ToListAsync());
        return all.First(match);
    }
}
