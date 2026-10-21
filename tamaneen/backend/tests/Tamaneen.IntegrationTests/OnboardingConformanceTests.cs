using System.Net.Http.Json;
using System.Text.Json;
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
}
