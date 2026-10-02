using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ProposalSystem.Api.Data;

namespace ProposalSystem.Api.Tests;

/// <summary>Departments are maintained in Settings and chosen from a list on user accounts.</summary>
public sealed class DepartmentTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static object NewUser(string code, string department) => new
    {
        userCode = code, arabicName = "موظف جديد", englishName = "New Employee", email = $"{code}@shjcharity.ae",
        initialPassword = "Passw0rd!", role = "Employee", phoneNumber = "0", department, jobTitle = "أخصائي",
    };

    [Fact]
    public async Task The_list_starts_from_the_departments_existing_accounts_already_have()
    {
        var admin = await _host.LoginAsync("EMP-1001");
        var names = (await admin.GetJsonAsync<JsonElement[]>("/api/departments")).Select(d => d.GetProperty("name").GetString()).ToList();
        Assert.Contains("الإدارة المالية", names);
        Assert.Contains("إدارة التميز المؤسسي", names);
        Assert.Contains("إدارة المشاريع الخارجية", names);
    }

    [Fact]
    public async Task A_user_can_only_be_given_a_department_from_the_settings_list()
    {
        var admin = await _host.LoginAsync("EMP-1001");

        var typo = await admin.PostAsync("/api/users", NewUser("EMP-5001", "الادارة الماليه"));
        Assert.Equal(HttpStatusCode.BadRequest, typo.StatusCode);

        await admin.PostJsonAsync("/api/departments", new { name = "إدارة الموارد البشرية" });
        var ok = await admin.PostJsonAsync("/api/users", NewUser("EMP-5002", "إدارة الموارد البشرية"));
        Assert.Equal("إدارة الموارد البشرية", ok.GetProperty("department").GetString());
    }

    [Fact]
    public async Task Renaming_a_department_moves_its_users_and_proposals_with_it()
    {
        var admin = await _host.LoginAsync("EMP-1001");
        var employee = await _host.LoginAsync("EMP-2001");
        var proposal = await employee.PostJsonAsync("/api/proposals", new
        {
            title = "مقترح لاختبار تغيير اسم الإدارة",
            implementationMechanism = "ربط نظام التبرعات بالبريد الإلكتروني لإرسال الإيصالات",
            submissionReasons = "تقليل الوقت المهدر يومياً في إصدار الإيصالات يدوياً",
            customFields = new Dictionary<string, string> { ["impactCost"] = "true" },
        });

        var depts = await admin.GetJsonAsync<JsonElement[]>("/api/departments");
        var projects = depts.Single(d => d.GetProperty("name").GetString() == "إدارة المشاريع الخارجية");
        var res = await admin.PutAsync($"/api/departments/{projects.GetProperty("id").GetInt32()}", new { name = "إدارة المشاريع الدولية", isActive = true });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var me = await employee.GetJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("إدارة المشاريع الدولية", me.GetProperty("department").GetString());
        var p = await employee.GetJsonAsync<JsonElement>($"/api/proposals/{proposal.GetProperty("id").GetInt32()}");
        Assert.Equal("إدارة المشاريع الدولية", p.GetProperty("department").GetString());
    }

    [Fact]
    public async Task A_department_in_use_cannot_be_deleted_and_a_deactivated_one_cannot_be_assigned()
    {
        var admin = await _host.LoginAsync("EMP-1001");
        var finance = (await admin.GetJsonAsync<JsonElement[]>("/api/departments")).Single(d => d.GetProperty("name").GetString() == "الإدارة المالية");
        var id = finance.GetProperty("id").GetInt32();
        Assert.Equal(1, finance.GetProperty("userCount").GetInt32());

        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/departments/{id}")).StatusCode);

        await admin.PutAsync($"/api/departments/{id}", new { name = "الإدارة المالية", isActive = false });
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/users", NewUser("EMP-5003", "الإدارة المالية"))).StatusCode);
        Assert.DoesNotContain("الإدارة المالية", await admin.GetJsonAsync<string[]>("/api/users/departments"));

        // The member already in that department can still be edited without being moved.
        var users = await admin.GetJsonAsync<JsonElement[]>("/api/users");
        var qasim = users.Single(u => u.GetProperty("userCode").GetString() == "EMP-1003");
        var edit = await admin.PutAsync($"/api/users/{qasim.GetProperty("id").GetInt32()}", new
        {
            id = qasim.GetProperty("id").GetInt32(), userCode = "EMP-1003", arabicName = "قاسم الذوخي", englishName = "Qasim",
            email = "emp-1003@shjcharity.ae", role = "CommitteeMember", phoneNumber = "0", department = "الإدارة المالية", jobTitle = "عضو",
        });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        // An unused department can be deleted.
        var temp = await admin.PostJsonAsync("/api/departments", new { name = "إدارة مؤقتة" });
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/departments/{temp.GetProperty("id").GetInt32()}")).StatusCode);
    }

    [Fact]
    public async Task Only_the_administrator_changes_the_list()
    {
        var employee = await _host.LoginAsync("EMP-2001");
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PostAsync("/api/departments", new { name = "إدارة جديدة" })).StatusCode);
        Assert.NotEmpty(await employee.GetJsonAsync<JsonElement[]>("/api/departments?activeOnly=true"));
    }

    [Fact]
    public async Task An_existing_database_without_the_new_table_gets_it_at_startup()
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE \"Departments\"");

        var created = await SchemaUpgrader.CreateMissingTablesAsync(db, NullLogger.Instance);

        Assert.Equal(["Departments"], created);
        await DbSeeder.EnsureDepartmentsAsync(db);
        Assert.True(await db.Departments.AnyAsync());
    }
}
