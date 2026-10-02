using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ProposalSystem.Api.Tests;

/// <summary>Regression tests for the defects found in the team code review.</summary>
public sealed class ReviewFindingsTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static object NewProposal(string title) => new
    {
        title,
        implementationMechanism = "ربط نظام التبرعات بالبريد الإلكتروني لإرسال الإيصالات تلقائياً",
        submissionReasons = "تقليل الوقت المهدر يومياً في إصدار الإيصالات يدوياً",
        customFields = new Dictionary<string, string> { ["impactCost"] = "true" },
    };

    private static async Task<int> IdOf(ApiClient client) =>
        (await client.GetJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt32();

    [Fact]
    public async Task Concurrent_submissions_all_succeed_with_unique_codes()
    {
        var employee = await _host.LoginAsync("EMP-2001");
        var responses = await Task.WhenAll(Enumerable.Range(1, 8)
            .Select(i => employee.PostAsync("/api/proposals", NewProposal($"مقترح متزامن رقم {i}"))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var codes = new List<string>();
        foreach (var r in responses)
            codes.Add((await r.Content.ReadFromJsonAsync<JsonElement>(ApiClient.Json)).GetProperty("proposalCode").GetString()!);
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public async Task Suspending_the_last_unsigned_committee_member_completes_the_committee_stage()
    {
        var admin = await _host.LoginAsync("EMP-1001");
        await admin.PostJsonAsync("/api/users", new
        {
            userCode = "EMP-3001", arabicName = "عضو لجنة ثانٍ", englishName = "Second Member", email = "m2@shjcharity.ae",
            initialPassword = "Passw0rd!", role = "CommitteeMember", phoneNumber = "0", department = "الإدارة المالية", jobTitle = "عضو",
        });
        var employee = await _host.LoginAsync("EMP-2001");
        var id = (await employee.PostJsonAsync("/api/proposals", NewProposal("مقترح بلجنة من عضوين"))).GetProperty("id").GetInt32();

        var screener = await _host.LoginAsync("EMP-1002");
        await screener.PostJsonAsync($"/api/proposals/{id}/screen", new { action = "Approve" });
        var committee = await _host.LoginAsync("EMP-1003");
        await committee.PostJsonAsync($"/api/proposals/{id}/committee-study", new { classification = "Good", committeeStudy = "دراسة", committeeRecommendation = "اعتماد" });
        await committee.PostJsonAsync($"/api/proposals/{id}/committee-sign", new { memberId = await IdOf(committee) });

        // The second member leaves before signing.
        var users = await admin.GetJsonAsync<JsonElement[]>("/api/users");
        var second = users.Single(u => u.GetProperty("userCode").GetString() == "EMP-3001").GetProperty("id").GetInt32();
        await admin.PostJsonAsync($"/api/users/{second}/suspend");

        var p = await admin.GetJsonAsync<JsonElement>($"/api/proposals/{id}");
        Assert.Equal("PendingExecutiveDecision", p.GetProperty("status").GetString());
        Assert.False(p.GetProperty("submitterHidden").GetBoolean() && p.GetProperty("identityRevealedAt").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task A_rejected_upload_leaves_no_files_on_disk()
    {
        var employee = await _host.LoginAsync("EMP-2001");
        var id = (await employee.PostJsonAsync("/api/proposals", NewProposal("مقترح بمرفقات"))).GetProperty("id").GetInt32();

        var res = await employee.UploadAsync($"/api/proposals/{id}/attachments",
            ("plan.pdf", "%PDF-1.4 ok"u8.ToArray()),
            ("virus.exe", "MZ"u8.ToArray()));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True(!Directory.Exists(_host.AttachmentDir) || Directory.GetFiles(_host.AttachmentDir).Length == 0,
            "the valid file of a refused batch must not be left orphaned on disk");
    }

    [Fact]
    public async Task Deleting_a_user_with_impact_history_is_refused_cleanly()
    {
        var admin = await _host.LoginAsync("EMP-1001");
        var newUser = await admin.PostJsonAsync("/api/users", new
        {
            userCode = "EMP-4001", arabicName = "مدير مؤقت", englishName = "Temp Admin", email = "t@shjcharity.ae",
            initialPassword = "Passw0rd!", role = "Admin", phoneNumber = "0", department = "إدارة التميز المؤسسي", jobTitle = "مدير",
        });
        var tempAdmin = await _host.LoginAsync("EMP-4001");

        // Full path to an approved proposal whose impact the temporary admin measures and verifies.
        var employee = await _host.LoginAsync("EMP-2001");
        var id = (await employee.PostJsonAsync("/api/proposals", NewProposal("مقترح لقياس الأثر"))).GetProperty("id").GetInt32();
        await (await _host.LoginAsync("EMP-1002")).PostJsonAsync($"/api/proposals/{id}/screen", new { action = "Approve" });
        var committee = await _host.LoginAsync("EMP-1003");
        await committee.PostJsonAsync($"/api/proposals/{id}/committee-study", new { classification = "Good", committeeStudy = "دراسة", committeeRecommendation = "اعتماد" });
        await committee.PostJsonAsync($"/api/proposals/{id}/committee-sign", new { memberId = await IdOf(committee) });
        await admin.PostJsonAsync($"/api/proposals/{id}/executive-decision", new { decision = "Accepted" });
        var put = await tempAdmin.PutAsync($"/api/proposals/{id}/impact", new
        {
            actualAnnualSavings = 1000m, implementationCost = 500m, targetAchievementPercent = 80m, impactRating = 3, summary = "أثر",
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var res = await admin.DeleteAsync($"/api/users/{newUser.GetProperty("id").GetInt32()}");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public void Inconsistent_escalation_settings_stop_the_application_at_startup()
    {
        using var host = new TestHost(("Sla:Escalation:Level1AfterHours", "96"), ("Sla:Escalation:Level2AfterHours", "48"));
        var ex = Record.Exception(() => host.CreateClient());
        Assert.NotNull(ex);
        Assert.Contains("Level2AfterHours", ex.ToString(), StringComparison.Ordinal);
    }
}

public sealed class FollowUpTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Only_one_instance_holds_the_sla_monitor_lease_until_it_expires()
    {
        using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(_host.Services);
        var leases = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<Workflow.JobLeaseService>(scope.ServiceProvider);
        var term = TimeSpan.FromMinutes(30);

        Assert.True(await leases.TryAcquireAsync("sla-monitor", term, "server-a"));
        Assert.False(await leases.TryAcquireAsync("sla-monitor", term, "server-b"));
        Assert.True(await leases.TryAcquireAsync("sla-monitor", term, "server-a")); // renewal

        _host.Clock.Advance(TimeSpan.FromMinutes(31)); // server-a died
        Assert.True(await leases.TryAcquireAsync("sla-monitor", term, "server-b"));
        Assert.False(await leases.TryAcquireAsync("sla-monitor", term, "server-a"));
    }

    [Fact]
    public async Task Suspending_a_user_ends_their_open_sessions_immediately()
    {
        var employee = await _host.LoginAsync("EMP-2001");
        var admin = await _host.LoginAsync("EMP-1001");
        var id = (await employee.GetJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt32();

        await admin.PostJsonAsync($"/api/users/{id}/suspend");

        Assert.Equal(HttpStatusCode.Unauthorized, (await employee.GetAsync("/api/auth/me")).StatusCode);
    }
}
