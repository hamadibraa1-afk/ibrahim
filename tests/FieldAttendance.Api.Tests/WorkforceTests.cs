using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FieldAttendance.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldAttendance.Api.Tests;

/// <summary>Every employee has an HR record; the workforce decides who schedules them, not whether HR sees them.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class WorkforceTests(ApiFactory api)
{
    [Fact]
    public async Task Hr_sees_field_staff_and_their_salaries_separately_from_office_staff()
    {
        var hr = await api.ClientForAsync("1005");

        var field = (await hr.GetFromJsonAsync<JsonElement[]>("/api/hr/employees?workforce=Field"))!;
        var office = (await hr.GetFromJsonAsync<JsonElement[]>("/api/hr/employees?workforce=Office"))!;

        // The 20 seeded collectors, plus any a test in the same run has added.
        Assert.True(field.Length >= 20);
        Assert.All(field, r => Assert.Equal("Collector", r.GetProperty("role").GetString()));
        Assert.DoesNotContain(office, r => r.GetProperty("role").GetString() == "Collector");

        var detail = await hr.GetFromJsonAsync<JsonElement>($"/api/hr/employees/{field[0].GetProperty("id").GetGuid()}");
        Assert.True(detail.GetProperty("basicSalary").GetDecimal() > 0);
    }

    [Fact]
    public async Task A_collector_reports_to_the_supervisor_of_their_site()
    {
        var collector = await api.UserIdAsync("2001");
        var supervisor = await api.UserIdAsync("1002");

        var manager = await api.QueryAsync(db => db.EmployeeProfiles.Where(p => p.UserId == collector).Select(p => p.ManagerId).SingleAsync());

        Assert.Equal(supervisor, manager);
    }

    [Fact]
    public async Task Hr_adds_a_field_employee_with_an_account_that_signs_in_to_the_collector_app()
    {
        var hr = await api.ClientForAsync("1005");
        var body = await NewEmployeeAsync("9101", workforce: "Field", scheduleId: null);

        var response = await hr.PostAsJsonAsync("/api/hr/employees", body);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var created = await api.QueryAsync(db => (
            from u in db.Users join p in db.EmployeeProfiles on u.Id equals p.UserId
            where u.EmployeeNumber == "9101" select new { u.Role, p.Workforce }).SingleAsync());
        Assert.Equal(UserRole.Collector, created.Role);
        Assert.Equal(Workforce.Field, created.Workforce);
    }

    [Fact]
    public async Task A_field_employee_cannot_be_put_on_an_office_work_schedule()
    {
        var hr = await api.ClientForAsync("1005");
        var schedule = await api.QueryAsync(db => db.WorkSchedules.Select(s => s.Id).FirstAsync());

        var response = await hr.PostAsJsonAsync("/api/hr/employees", await NewEmployeeAsync("9102", "Field", schedule));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("profile.field_schedule", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Saving_a_field_employees_hr_record_keeps_their_site_roster()
    {
        var collector = await api.UserIdAsync("2002");
        var before = await ActiveAssignmentsAsync(collector);
        Assert.NotEqual(0, before);
        var profileId = await api.QueryAsync(db => db.EmployeeProfiles.Where(p => p.UserId == collector).Select(p => p.Id).SingleAsync());
        var otherBranch = await api.QueryAsync(db => db.Locations
            .Where(l => l.Kind == LocationKind.Office).OrderBy(l => l.NameEn).Select(l => l.Id).FirstAsync());
        var hr = await api.ClientForAsync("1005");
        var d = await hr.GetFromJsonAsync<JsonElement>($"/api/hr/employees/{profileId}");
        var row = d.GetProperty("row");

        // A branch change is what triggers re-applying the office schedule for office staff.
        var response = await hr.PutAsJsonAsync($"/api/hr/employees/{profileId}", new
        {
            fullName = row.GetProperty("fullName").GetString(), phone = row.GetProperty("phone").GetString(),
            email = (string?)null, preferredLanguage = "ar", branchLocationId = otherBranch,
            departmentId = row.GetProperty("departmentId").GetGuid(), sectionId = (Guid?)null,
            jobTitleId = d.GetProperty("jobTitleId").GetGuid(), gradeId = d.GetProperty("gradeId").GetGuid(),
            contractTypeId = d.GetProperty("contractTypeId").GetGuid(), managerId = d.GetProperty("managerId").GetGuid(),
            hireDate = row.GetProperty("hireDate").GetString(), rowVersion = d.GetProperty("rowVersion").GetString(),
        });

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal(before, await ActiveAssignmentsAsync(collector));
    }

    [Theory]
    [InlineData("Collector")]
    [InlineData("SystemAdmin")]
    public async Task The_field_module_creates_no_accounts_so_none_lacks_an_hr_record(string role)
    {
        var admin = await api.ClientForAsync("1001");

        var response = await admin.PostAsJsonAsync("/api/employees", new
        {
            fullName = "بلا سجل", employeeNumber = "9103", email = (string?)null, phone = "0501112233",
            role, preferredLanguage = "ar", password = "Test@1234",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("user.create_in_hr", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    /// <summary>Managers, supervisors and the administrator are employees too: each has an HR record, a check-in and a profile.</summary>
    [Fact]
    public async Task Every_account_is_an_employee_with_an_hr_record()
    {
        var without = await api.QueryAsync(db => db.Users
            .Where(u => u.IsActive && u.EmployeeNumber != ApiFactory.LegacyAccount && !db.EmployeeProfiles.Any(p => p.UserId == u.Id))
            .Select(u => u.EmployeeNumber).ToListAsync());

        Assert.Empty(without);
    }

    private Task<int> ActiveAssignmentsAsync(Guid employee) =>
        api.QueryAsync(db => db.Assignments.CountAsync(a => a.EmployeeId == employee && a.IsActive && a.EndDate == null));

    private async Task<object> NewEmployeeAsync(string number, string workforce, Guid? scheduleId)
    {
        var refs = await api.QueryAsync(async db => new
        {
            Branch = await db.Locations.Where(l => l.Kind == LocationKind.Office).Select(l => l.Id).FirstAsync(),
            Department = await db.Departments.Select(x => x.Id).FirstAsync(),
        });
        return new
        {
            fullName = "موظف ميداني جديد", employeeNumber = number, phone = "0501234567", email = (string?)null,
            role = "Employee", preferredLanguage = "ar", password = "Test@1234",
            branchLocationId = refs.Branch, departmentId = refs.Department, workScheduleId = scheduleId,
            hireDate = "2026-09-01", basicSalary = 4500m, workforce,
        };
    }
}
