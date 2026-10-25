using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Tamaneen.Api;
using static Tamaneen.IntegrationTests.JourneyHosts;

namespace Tamaneen.IntegrationTests;

/// <summary>
/// Onboarding and intake rules the patient app relies on: the sign-up pause (EC-26), the intake message
/// limit and idempotent retries (EC-19, PB-21/27), the form mode when the assistant is down (PB-28), the
/// expected wait after asking for a person (PB-25) and the optional reason for "not a fit" (PB-36).
/// </summary>
[Collection(SqlServerGroup.Name)]
public sealed class OnboardingConformanceTests : IAsyncLifetime
{
    private readonly SqlServerFixture _sql;
    private JourneyHosts _hosts = null!;

    public OnboardingConformanceTests(SqlServerFixture sql) => _sql = sql;

    public Task InitializeAsync()
    {
        _hosts = new JourneyHosts(_sql);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _hosts.DisposeAsync();

    [Fact]
    public async Task Paused_sign_ups_are_announced_in_the_config_and_refused_in_both_languages()
    {
        using var open = _hosts.ApiClient("ar");
        (await Read(await open.GetAsync("/v1/meta/config"))).GetProperty("signupsOpen").GetBoolean().Should().BeTrue();
        var token = (await Read(await open.PostAsJsonAsync("/v1/auth/eligibility", new { isAdult = true, cityCode = "RUH" }))).GetProperty("eligibilityToken").GetString();

        await using var paused = _hosts.Api.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.PostConfigure<PlatformOptions>(o => o.SignupsOpen = false)));
        using var ar = paused.CreateClient();
        ar.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ar");
        using var en = paused.CreateClient();
        en.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");

        (await Read(await ar.GetAsync("/v1/meta/config"))).GetProperty("signupsOpen").GetBoolean().Should().BeFalse();
        var refused = await Problem(await ar.PostAsJsonAsync("/v1/auth/eligibility", new { isAdult = true, cityCode = "RUH" }), 503);
        refused.GetProperty("code").GetString().Should().Be("error.auth.signups_paused");
        refused.GetProperty("detail").GetString().Should().StartWith("التسجيل الجديد متوقف الآن");

        // A token issued before the pause does not create an account either.
        var signup = await Problem(await en.PostAsJsonAsync("/v1/auth/signup", new { eligibilityToken = token, nickname = "Calm Palm", contactType = "email", contact = $"paused-{Guid.NewGuid():N}@example.com", password = "Quiet-Morning-Walk-77", language = "en" }), 503);
        signup.GetProperty("detail").GetString().Should().StartWith("New sign-ups are paused right now.");
    }

    [Fact]
    public async Task Intake_limits_retries_form_mode_person_request_and_not_a_fit_reason()
    {
        var access = await SignUpAsync("en", $"intake-{Guid.NewGuid():N}@example.com");
        using var me = _hosts.ApiClient("en", access);
        await Read(await me.PutAsJsonAsync("/v1/me/consents", new { items = new[] { "platform", "ai", "share_with_therapist" }.Select(k => new { key = k, granted = true }) }));
        await Read(await PutSafetyInfoAsync(me, new { shareLocation = false }));
        await Read(await me.PutAsJsonAsync("/v1/me/path", new { path = "intake" }));
        var intake = await Read(await me.PostAsync("/v1/intake", null));
        var intakeId = intake.GetProperty("intakeId").GetGuid();
        intake.GetProperty("formMode").GetBoolean().Should().BeFalse();

        // EC-19: more than 2,000 characters is refused with the limit in the message.
        var tooLong = await Problem(await me.PostAsJsonAsync($"/v1/intake/{intakeId}/messages", new { text = new string('a', 2001) }), 400);
        tooLong.GetProperty("code").GetString().Should().Be("error.ai.message_invalid");
        tooLong.GetProperty("detail").GetString().Should().Contain("2000");

        // PB-21/27: the same client message id twice stores and answers one turn.
        var clientMessageId = Guid.NewGuid();
        var first = await Read(await me.PostAsJsonAsync($"/v1/intake/{intakeId}/messages", new { text = "Work has been stressful lately", clientMessageId }));
        var retry = await Read(await me.PostAsJsonAsync($"/v1/intake/{intakeId}/messages", new { text = "Work has been stressful lately", clientMessageId }));
        retry.GetProperty("messages").GetArrayLength().Should().Be(first.GetProperty("messages").GetArrayLength()).And.Be(3);
        retry.GetProperty("messages").EnumerateArray().Count(m => m.GetProperty("role").GetString() == "patient").Should().Be(1);

        // PB-25: asking for a person says when the team will have reached out.
        var contact = await Read(await me.PostAsync("/v1/me/contact-request?source=intake", null));
        contact.GetProperty("withinHours").GetInt32().Should().Be(24);
        (contact.GetProperty("expectedBy").GetDateTimeOffset() - contact.GetProperty("requestedAt").GetDateTimeOffset()).Should().Be(TimeSpan.FromHours(24));

        // PB-28: after one turn the patient switches to the form; the questionnaires start at once and the
        // patient can finish without more conversation; the summary is theirs to write.
        intake = await Read(await me.PostAsync($"/v1/intake/{intakeId}/form-mode", null));
        intake.GetProperty("formMode").GetBoolean().Should().BeTrue();
        intake.GetProperty("progress").GetInt32().Should().BeGreaterThan(0, "progress is kept");
        intake.GetProperty("pendingQuestion").GetProperty("instrument").GetString().Should().Be("phq9");
        foreach (var (instrument, items) in new[] { ("phq9", 9), ("gad7", 7) })
        {
            for (var item = 1; item <= items; item++)
            {
                intake = await Read(await me.PostAsJsonAsync($"/v1/intake/{intakeId}/answers", new { instrument, item, value = (int?)(item == 9 ? 0 : 1) }));
            }
        }

        intake.GetProperty("canFinish").GetBoolean().Should().BeTrue();
        var summary = await Read(await me.PostAsync($"/v1/intake/{intakeId}/complete", null));
        summary.GetProperty("generatedByAi").GetBoolean().Should().BeFalse();

        // P05: sections follow the same 2,000-character limit as the app.
        await Problem(await me.PutAsJsonAsync($"/v1/intake/{intakeId}/summary", new { sections = new Dictionary<string, string> { ["concern"] = new string('b', 2001) } }), 400);
        await Read(await me.PutAsJsonAsync($"/v1/intake/{intakeId}/summary", new { sections = new Dictionary<string, string> { ["concern"] = "Stress at work and poor sleep.", ["goals"] = "Sleep better." } }));
        await Read(await me.PostAsync($"/v1/intake/{intakeId}/submit", null));

        // PB-36: "not a fit" takes an optional reason from a fixed list and removes only that suggestion.
        using var supervisor = _hosts.ApiClient("en", await _hosts.StaffSignInAsync("fahad@staff.tamaneen.dev"));
        var pseudonym = (await Read(await me.GetAsync("/v1/me"))).GetProperty("pseudonymId").GetString();
        var review = (await Read(await supervisor.GetAsync("/v1/staff/triage"))).EnumerateArray().Single(t => t.GetProperty("pseudonymId").GetString() == pseudonym);
        await Read(await supervisor.PostAsJsonAsync($"/v1/staff/triage/{review.GetProperty("id").GetGuid()}/review", new { riskLevel = 1, focus = new[] { "anxiety", "stress" } }));
        var matches = await Read(await me.GetAsync("/v1/matches"));
        var count = matches.GetProperty("items").GetArrayLength();
        count.Should().BeGreaterThan(1);
        var firstId = matches.GetProperty("items")[0].GetProperty("therapist").GetProperty("id").GetGuid();
        var secondId = matches.GetProperty("items")[1].GetProperty("therapist").GetProperty("id").GetGuid();
        await Problem(await me.PostAsJsonAsync($"/v1/matches/{firstId}/dismiss", new { reason = "because" }), 400);
        matches = await Read(await me.PostAsJsonAsync($"/v1/matches/{firstId}/dismiss", new { reason = "schedule" }));
        matches.GetProperty("items").GetArrayLength().Should().Be(count - 1);
        matches = await Read(await me.PostAsync($"/v1/matches/{secondId}/dismiss", null));
        matches.GetProperty("items").EnumerateArray().Should().NotContain(m => m.GetProperty("therapist").GetProperty("id").GetGuid() == secondId);
    }

    private async Task<string> SignUpAsync(string language, string email)
    {
        using var anon = _hosts.ApiClient(language);
        var eligibility = await Read(await anon.PostAsJsonAsync("/v1/auth/eligibility", new { isAdult = true, cityCode = "RUH" }));
        var challenge = await Read(await anon.PostAsJsonAsync("/v1/auth/signup", new { eligibilityToken = eligibility.GetProperty("eligibilityToken").GetString(), nickname = "Calm Palm", contactType = "email", contact = email, password = "Quiet-Morning-Walk-77", language }));
        var verified = await Read(await anon.PostAsJsonAsync("/v1/auth/signup/verify", new { challengeId = challenge.GetProperty("challengeId").GetGuid(), code = _hosts.CodeSentTo(email) }));
        return verified.GetProperty("tokens").GetProperty("accessToken").GetString()!;
    }
}
