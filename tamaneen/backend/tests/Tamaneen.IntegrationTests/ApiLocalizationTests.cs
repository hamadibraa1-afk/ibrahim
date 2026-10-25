using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tamaneen.SharedKernel;

namespace Tamaneen.IntegrationTests;

[Collection(SqlServerGroup.Name)]
public sealed class ApiLocalizationTests : IAsyncLifetime
{
    private readonly SqlServerFixture _sql;
    private ApiFactory _api = null!;

    public ApiLocalizationTests(SqlServerFixture sql) => _sql = sql;

    public Task InitializeAsync()
    {
        _api = new ApiFactory(_sql);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Maintenance_pauses_patient_endpoints_with_a_localized_503_but_keeps_config_sign_in_and_crisis_up()
    {
        using var client = _api.CreateClient();
        _api.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Tamaneen.Api.PlatformOptions>>().CurrentValue.Maintenance = true;

        using var me = new HttpRequestMessage(HttpMethod.Get, "/v1/me");
        me.Headers.TryAddWithoutValidation("Accept-Language", "en");
        using var paused = await client.SendAsync(me);
        paused.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var problem = await paused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be(ErrorCodes.Maintenance);
        problem.GetProperty("detail").GetString().Should().StartWith("We are doing scheduled maintenance");

        (await client.GetAsync("/v1/meta/config")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/v1/crisis/status")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "crisis endpoints are not paused, only signed in");
        (await client.PostAsJsonAsync("/v1/auth/eligibility", new { isAdult = true, cityCode = "RUH" })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("ar", "ar", "rtl", "مرحبًا بك في طمأنينة.")]
    [InlineData("ar-SA,ar;q=0.9", "ar", "rtl", "مرحبًا بك في طمأنينة.")]
    [InlineData("en-US,en;q=0.9", "en", "ltr", "Welcome to Tamaneen.")]
    [InlineData("fr-FR", "ar", "rtl", "مرحبًا بك في طمأنينة.")]
    [InlineData(null, "ar", "rtl", "مرحبًا بك في طمأنينة.")]
    public async Task Locale_follows_accept_language_with_arabic_default(string? header, string language, string dir, string message)
    {
        using var client = _api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/meta/locale");
        if (header is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", header);
        }

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentLanguage.Should().Contain(language);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("language").GetString().Should().Be(language);
        body.GetProperty("dir").GetString().Should().Be(dir);
        body.GetProperty("message").GetString().Should().Be(message);
        body.GetProperty("supported").GetArrayLength().Should().Be(2);
    }

    [Theory]
    [InlineData("ar", "لم نجد ما تبحث عنه.", "rtl")]
    [InlineData("en", "We couldn't find what you're looking for.", "ltr")]
    public async Task Errors_are_problem_details_in_the_requested_language(string language, string title, string dir)
    {
        using var client = _api.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        using var response = await client.GetAsync("/v1/does-not-exist");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be(title);
        problem.GetProperty("lang").GetString().Should().Be(language);
        problem.GetProperty("dir").GetString().Should().Be(dir);
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("ar", "يجب تسجيل الدخول.")]
    [InlineData("en", "You need to sign in.")]
    public async Task Unauthenticated_calls_get_a_localized_401(string language, string title)
    {
        using var client = _api.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        using var response = await client.PutAsJsonAsync("/v1/me/language", new { language = "en" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString().Should().Be(title);
    }

    [Fact]
    public async Task Changing_language_saves_it_audits_it_and_confirms_in_the_new_language()
    {
        var patient = await _sql.CreatePatientAsync("نورة", language: "ar");
        using var client = _api.ClientFor(patient.Id, AppRoles.Patient, patient.PseudonymId);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ar");

        using var toEnglish = await client.PutAsJsonAsync("/v1/me/language", new { language = "en" });
        toEnglish.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await toEnglish.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("language").GetString().Should().Be("en");
        body.GetProperty("dir").GetString().Should().Be("ltr");
        body.GetProperty("message").GetString().Should().Be("Your language is now English.");

        using var toArabic = await client.PutAsJsonAsync("/v1/me/language", new { language = "ar" });
        (await toArabic.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString().Should().Be("أصبحت لغتك العربية.");

        await using var db = _sql.Context(StaticCurrentUser.Anonymous);
        (await db.UserAccounts.SingleAsync(a => a.Id == patient.Id)).PreferredLanguage.Should().Be("ar");
        (await db.AuditEntries.CountAsync(a => a.ActorUserId == patient.Id && a.Action == "user.language_changed")).Should().Be(2);
    }

    [Theory]
    [InlineData("ar", "اللغة «fr» غير مدعومة. اختر العربية (ar) أو الإنجليزية (en).")]
    [InlineData("en", "The language 'fr' is not supported. Choose Arabic (ar) or English (en).")]
    public async Task Unsupported_language_is_a_localized_400_with_a_stable_code(string language, string detail)
    {
        var patient = await _sql.CreatePatientAsync("فهد");
        using var client = _api.ClientFor(patient.Id, AppRoles.Patient, patient.PseudonymId);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        using var response = await client.PutAsJsonAsync("/v1/me/language", new { language = "fr" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be(ErrorCodes.UnsupportedLanguage);
        problem.GetProperty("detail").GetString().Should().Be(detail);
    }

    [Theory]
    [InlineData("ar", "اللغة مطلوبة.")]
    [InlineData("en", "Language is required.")]
    public async Task Missing_language_is_a_localized_validation_problem(string language, string message)
    {
        var patient = await _sql.CreatePatientAsync("هيا");
        using var client = _api.ClientFor(patient.Id, AppRoles.Patient, patient.PseudonymId);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        using var response = await client.PutAsJsonAsync("/v1/me/language", new { language = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").GetProperty("language")[0].GetString().Should().Be(message);
    }

    [Fact]
    public async Task Expired_or_forged_tokens_are_rejected()
    {
        var patient = await _sql.CreatePatientAsync("ريم");
        using var client = _api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", _api.Token(patient.Id, AppRoles.Patient, patient.PseudonymId, TimeSpan.FromMinutes(-5)));
        (await client.PutAsJsonAsync("/v1/me/language", new { language = "en" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var forged = _api.Token(patient.Id, AppRoles.Patient, patient.PseudonymId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", forged[..^4] + "AAAA");
        (await client.PutAsJsonAsync("/v1/me/language", new { language = "en" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Readiness_checks_the_database()
    {
        using var client = _api.CreateClient();
        (await client.GetStringAsync("/health/ready")).Should().Be("Healthy");
    }

    [Fact]
    public void Arabic_and_english_resources_have_the_same_keys_and_cover_every_error_code()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Tamaneen.Api", "Resources");
        static HashSet<string> Keys(string path) =>
            XDocument.Load(path).Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet();
        var en = Keys(Path.Combine(dir, "SharedResource.resx"));
        var ar = Keys(Path.Combine(dir, "SharedResource.ar.resx"));
        ar.Should().BeEquivalentTo(en);

        var codes = typeof(ErrorCodes).GetFields().Select(f => (string)f.GetValue(null)!);
        en.Should().Contain(codes);
    }
}
