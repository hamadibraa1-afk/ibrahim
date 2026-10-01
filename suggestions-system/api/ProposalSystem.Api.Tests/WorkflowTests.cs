using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ProposalSystem.Api.Tests;

public sealed class WorkflowTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    private async Task<(ApiClient Employee, int Id)> SubmitAsync(string title = "أتمتة إيصالات التبرعات الإلكترونية")
    {
        var employee = await _host.LoginAsync("EMP-2001");
        var fields = await employee.GetJsonAsync<JsonElement[]>("/api/form-settings?activeOnly=true");
        var custom = fields.Where(f => f.GetProperty("section").GetString() == "Impact")
            .ToDictionary(f => f.GetProperty("fieldKey").GetString()!, _ => "false");
        custom["impactCost"] = "true";
        var p = await employee.PostJsonAsync("/api/proposals", new
        {
            title,
            implementationMechanism = "ربط نظام التبرعات بالبريد الإلكتروني لإرسال الإيصالات تلقائياً",
            submissionReasons = "تقليل الوقت المهدر يومياً في إصدار الإيصالات يدوياً",
            customFields = custom,
        });
        return (employee, p.GetProperty("id").GetInt32());
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res) => await res.Content.ReadFromJsonAsync<JsonElement>(ApiClient.Json);

    // ------------------------------------------------------------------ blind screening

    [Fact]
    public async Task Screener_and_admin_never_see_the_proposer_before_the_reveal_stage()
    {
        var (employee, id) = await SubmitAsync();
        var screener = await _host.LoginAsync("EMP-1002");
        var admin = await _host.LoginAsync("EMP-1001");

        foreach (var reviewer in new[] { screener, admin })
        {
            var p = await reviewer.GetJsonAsync<JsonElement>($"/api/proposals/{id}");
            Assert.True(p.GetProperty("submitterHidden").GetBoolean());
            Assert.Equal(0, p.GetProperty("submitterId").GetInt32());
            Assert.DoesNotContain("أحمد", p.GetProperty("submitterName").GetString());

            var audit = await reviewer.GetJsonAsync<JsonElement[]>($"/api/audit/proposal/{id}");
            Assert.All(audit, a => Assert.DoesNotContain("أحمد", a.GetProperty("actorName").GetString()));

            var form = await (await reviewer.GetAsync($"/api/proposals/{id}/form-pdf")).Content.ReadAsStringAsync();
            Assert.DoesNotContain("EMP-2001", form);
            Assert.DoesNotContain("أحمد", form);
        }

        // Probing by name returns nothing for a blind reviewer.
        var hits = await screener.GetJsonAsync<JsonElement[]>($"/api/proposals?search={Uri.EscapeDataString("أحمد")}");
        Assert.Empty(hits);

        // The proposer still sees their own name.
        var own = await employee.GetJsonAsync<JsonElement>($"/api/proposals/{id}");
        Assert.False(own.GetProperty("submitterHidden").GetBoolean());
        Assert.Equal("أحمد سالم", own.GetProperty("submitterName").GetString());
    }

    [Fact]
    public async Task Identity_is_revealed_only_once_the_committee_has_signed()
    {
        var (_, id) = await SubmitAsync();
        var screener = await _host.LoginAsync("EMP-1002");
        var committee = await _host.LoginAsync("EMP-1003");

        await screener.PostJsonAsync($"/api/proposals/{id}/screen", new { action = "Approve" });
        var withCommittee = await committee.GetJsonAsync<JsonElement>($"/api/proposals/{id}");
        Assert.True(withCommittee.GetProperty("submitterHidden").GetBoolean());

        await committee.PostJsonAsync($"/api/proposals/{id}/committee-study", new
        {
            classification = "Excellent", committeeStudy = "دراسة وافية", committeeRecommendation = "نوصي بالاعتماد",
        });
        var me = await committee.GetJsonAsync<JsonElement>("/api/auth/me");
        var signed = await committee.PostJsonAsync($"/api/proposals/{id}/committee-sign", new { memberId = me.GetProperty("id").GetInt32() });

        Assert.Equal("PendingExecutiveDecision", signed.GetProperty("status").GetString());
        Assert.False(signed.GetProperty("submitterHidden").GetBoolean());
        Assert.Equal("أحمد سالم", signed.GetProperty("submitterName").GetString());

        var audit = await screener.GetJsonAsync<JsonElement[]>($"/api/audit/proposal/{id}");
        Assert.Contains(audit, a => a.GetProperty("action").GetString() == "IdentityRevealed");
    }

    [Fact]
    public async Task A_proposal_rejected_at_screening_stays_anonymous_forever()
    {
        var (_, id) = await SubmitAsync();
        var screener = await _host.LoginAsync("EMP-1002");
        await screener.PostJsonAsync($"/api/proposals/{id}/screen", new { action = "Reject", notes = "خارج النطاق" });

        var p = await screener.GetJsonAsync<JsonElement>($"/api/proposals/{id}");
        Assert.Equal("Rejected", p.GetProperty("status").GetString());
        Assert.True(p.GetProperty("submitterHidden").GetBoolean());
    }

    [Fact]
    public async Task Nobody_screens_their_own_proposal()
    {
        var screener = await _host.LoginAsync("EMP-1002");
        var fields = await screener.GetJsonAsync<JsonElement[]>("/api/form-settings?activeOnly=true");
        var custom = fields.Where(f => f.GetProperty("section").GetString() == "Impact").ToDictionary(f => f.GetProperty("fieldKey").GetString()!, _ => "true");
        var p = await screener.PostJsonAsync("/api/proposals", new
        {
            title = "مقترح من موظف الفرز نفسه", implementationMechanism = new string('أ', 25), submissionReasons = new string('ب', 25), customFields = custom,
        });
        var res = await screener.PostAsync($"/api/proposals/{p.GetProperty("id").GetInt32()}/screen", new { action = "Approve" });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    // ------------------------------------------------------------------ SLA auto-escalation

    [Fact]
    public async Task Overdue_screening_escalates_to_the_line_manager_then_to_executive_management()
    {
        var (_, id) = await SubmitAsync();

        // Screening SLA is 72h. At deadline + 1h: reminder only.
        _host.Clock.Advance(TimeSpan.FromHours(73));
        var first = await _host.RunSlaAsync();
        Assert.Equal(1, first.Reminders);
        Assert.Equal(0, first.Level1Escalations);

        // Deadline + 48h: level 1 — rerouted to the screener's line manager (EMP-1004).
        _host.Clock.Advance(TimeSpan.FromHours(48));
        var second = await _host.RunSlaAsync();
        Assert.Equal(1, second.Level1Escalations);

        // Days pass in this test, so sign in fresh (idle sessions expire after 8 hours).
        var admin = await _host.LoginAsync("EMP-1001");
        var p = await admin.GetJsonAsync<JsonElement>($"/api/proposals/{id}");
        Assert.Equal(1, p.GetProperty("escalationLevel").GetInt32());
        Assert.Equal("مريم الشامسي", p.GetProperty("escalatedToName").GetString());

        // The manager is an ordinary employee, yet may now screen this one proposal — blind.
        var manager = await _host.LoginAsync("EMP-1004");
        var seen = await manager.GetJsonAsync<JsonElement>($"/api/proposals/{id}");
        Assert.True(seen.GetProperty("actions").GetProperty("canScreen").GetBoolean());
        Assert.True(seen.GetProperty("submitterHidden").GetBoolean());
        var notices = await manager.GetJsonAsync<JsonElement[]>("/api/notifications");
        Assert.Contains(notices, n => n.GetProperty("type").GetString() == "Escalation");
        var assigned = await manager.GetJsonAsync<JsonElement[]>("/api/proposals?assignedToMe=true");
        Assert.Single(assigned);

        // Running again changes nothing.
        var repeat = await _host.RunSlaAsync();
        Assert.Equal(new Workflow.SlaRunResult(0, 0, 0, 0, 0), repeat);

        // Deadline + 96h: level 2 — executive management.
        _host.Clock.Advance(TimeSpan.FromHours(48));
        var third = await _host.RunSlaAsync();
        Assert.Equal(1, third.Level2Escalations);
        admin = await _host.LoginAsync("EMP-1001");
        manager = await _host.LoginAsync("EMP-1004");
        p = await admin.GetJsonAsync<JsonElement>($"/api/proposals/{id}");
        Assert.Equal(2, p.GetProperty("escalationLevel").GetInt32());

        // Acting on it moves it on and clears the escalation for the next stage.
        var screened = await manager.PostJsonAsync($"/api/proposals/{id}/screen", new { action = "Approve" });
        Assert.Equal("WithCommittee", screened.GetProperty("status").GetString());
        Assert.Equal(0, screened.GetProperty("escalationLevel").GetInt32());
        Assert.Equal(JsonValueKind.Null, screened.GetProperty("escalatedToId").ValueKind);
    }

    // ------------------------------------------------------------------ impact / ROI

    [Fact]
    public async Task Approved_proposal_gets_an_impact_window_from_three_to_six_months()
    {
        var (employee, id) = await SubmitAsync();
        var screener = await _host.LoginAsync("EMP-1002");
        var committee = await _host.LoginAsync("EMP-1003");
        var admin = await _host.LoginAsync("EMP-1001");

        await screener.PostJsonAsync($"/api/proposals/{id}/screen", new { action = "Approve" });
        await committee.PostJsonAsync($"/api/proposals/{id}/committee-study", new { classification = "Good", committeeStudy = "دراسة", committeeRecommendation = "اعتماد" });
        var memberId = (await committee.GetJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt32();
        await committee.PostJsonAsync($"/api/proposals/{id}/committee-sign", new { memberId });
        var accepted = await admin.PostJsonAsync($"/api/proposals/{id}/executive-decision", new { decision = "Accepted" });
        Assert.Equal("Scheduled", accepted.GetProperty("impact").GetProperty("status").GetString());

        var employeeId = (await employee.GetJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt32();
        await admin.PostJsonAsync($"/api/proposals/{id}/assign-owner", new { ownerId = employeeId });

        var measurement = new { actualAnnualSavings = 80000m, actualAnnualRevenue = 20000m, implementationCost = 25000m, targetAchievementPercent = 95m, impactRating = 5, summary = "خفض زمن الإيصال من يومين إلى دقائق" };
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PutAsync($"/api/proposals/{id}/impact", measurement)).StatusCode);

        _host.Clock.Advance(TimeSpan.FromDays(93));
        Assert.Equal(1, (await _host.RunSlaAsync()).ImpactWindowsOpened);
        employee = await _host.LoginAsync("EMP-2001");
        admin = await _host.LoginAsync("EMP-1001");

        var measured = await Json(await employee.PutAsync($"/api/proposals/{id}/impact", measurement));
        Assert.Equal("Submitted", measured.GetProperty("impact").GetProperty("status").GetString());
        Assert.Equal(300m, measured.GetProperty("impact").GetProperty("roiPercent").GetDecimal());

        await admin.PostJsonAsync($"/api/proposals/{id}/impact/review", new { approve = true });
        var report = await admin.GetJsonAsync<JsonElement>("/api/impact/report");
        Assert.Equal(1, report.GetProperty("verified").GetInt32());
        Assert.Equal(100000m, report.GetProperty("totalAnnualBenefit").GetDecimal());
        Assert.Equal(300m, report.GetProperty("portfolioRoiPercent").GetDecimal());
    }

    [Fact]
    public async Task Missed_impact_measurement_is_escalated_after_six_months()
    {
        var (_, id) = await SubmitAsync();
        var screener = await _host.LoginAsync("EMP-1002");
        var committee = await _host.LoginAsync("EMP-1003");
        var admin = await _host.LoginAsync("EMP-1001");
        await screener.PostJsonAsync($"/api/proposals/{id}/screen", new { action = "Approve" });
        await committee.PostJsonAsync($"/api/proposals/{id}/committee-study", new { classification = "Good", committeeStudy = "دراسة", committeeRecommendation = "اعتماد" });
        await committee.PostJsonAsync($"/api/proposals/{id}/committee-sign", new { memberId = (await committee.GetJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt32() });
        await admin.PostJsonAsync($"/api/proposals/{id}/executive-decision", new { decision = "Accepted" });

        _host.Clock.Advance(TimeSpan.FromDays(186));
        var run = await _host.RunSlaAsync();
        Assert.Equal(1, run.ImpactWindowsOpened);
        Assert.Equal(1, run.ImpactEscalations);
        Assert.Equal(0, (await _host.RunSlaAsync()).ImpactEscalations);
    }

    // ------------------------------------------------------------------ session security

    [Fact]
    public async Task Session_cookie_is_httponly_secure_strict_and_the_body_has_no_token()
    {
        var client = await _host.LoginAsync("EMP-2001");
        var cookies = client.Last!.Headers.GetValues("Set-Cookie").ToList();
        var session = cookies.Single(c => c.StartsWith("sci_session=", StringComparison.Ordinal));
        Assert.Contains("httponly", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", session, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires", session, StringComparison.OrdinalIgnoreCase);

        var body = await client.Last.Content.ReadAsStringAsync();
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Session_slides_with_activity_and_dies_after_eight_idle_hours()
    {
        var client = await _host.LoginAsync("EMP-2001");

        _host.Clock.Advance(TimeSpan.FromHours(7));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.True(client.Last!.Headers.Contains("X-Session-Expires"));

        _host.Clock.Advance(TimeSpan.FromHours(7)); // 14h since login, 7h since last activity
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        _host.Clock.Advance(TimeSpan.FromHours(8) + TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task State_changes_without_the_csrf_header_are_refused()
    {
        var client = await _host.LoginAsync("EMP-2001");
        client.SendCsrf = false;
        var res = await client.PostAsync("/api/notifications/read-all");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        client.SendCsrf = true;
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/notifications/read-all")).StatusCode);
    }

    [Fact]
    public async Task Account_locks_after_five_wrong_passwords()
    {
        var anon = new ApiClient(_host.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false }));
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync("/api/auth/login", new { userCode = "EMP-1003", password = "wrong-1" })).StatusCode);
        var locked = await anon.PostAsync("/api/auth/login", new { userCode = "EMP-1003", password = Data.DbSeeder.DemoPassword });
        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);
    }

    [Fact]
    public async Task Cors_allows_only_the_configured_origin_with_credentials()
    {
        var http = _host.CreateClient();
        foreach (var (origin, allowed) in new[] { ("http://localhost:4200", true), ("https://evil.example", false) })
        {
            using var req = new HttpRequestMessage(HttpMethod.Options, "/api/proposals");
            req.Headers.Add("Origin", origin);
            req.Headers.Add("Access-Control-Request-Method", "POST");
            req.Headers.Add("Access-Control-Request-Headers", "content-type,x-xsrf-token");
            var res = await http.SendAsync(req);
            Assert.Equal(allowed, res.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) && values.Single() == origin);
            if (allowed)
                Assert.Equal("true", res.Headers.GetValues("Access-Control-Allow-Credentials").Single());
        }
    }

    [Fact]
    public async Task Hub_rejects_foreign_origins()
    {
        var client = await _host.LoginAsync("EMP-2001");
        var http = _host.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        using var req = new HttpRequestMessage(HttpMethod.Post, "/hubs/notifications/negotiate?negotiateVersion=1");
        req.Headers.Add("Origin", "https://evil.example");
        req.Headers.Add("Cookie", string.Join("; ", client.Cookies.Select(c => $"{c.Key}={c.Value}")));
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(req)).StatusCode);
    }
}
