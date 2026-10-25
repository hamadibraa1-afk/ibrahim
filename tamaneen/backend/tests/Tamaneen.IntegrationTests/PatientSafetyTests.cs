using System.Net.Http.Json;
using System.Text.Json;
using static Tamaneen.IntegrationTests.JourneyHosts;

namespace Tamaneen.IntegrationTests;

/// <summary>
/// Patient-safety rules through the real API and crisis worker: idempotent Help now (PB-26, EC-04), the
/// per-answer safety check (P23), and the safety data rules of P11, P21/P34 and P37.
/// </summary>
[Collection(SqlServerGroup.Name)]
public sealed class PatientSafetyTests : IAsyncLifetime
{
    private readonly SqlServerFixture _sql;
    private JourneyHosts _hosts = null!;

    public PatientSafetyTests(SqlServerFixture sql) => _sql = sql;

    public Task InitializeAsync()
    {
        _hosts = new JourneyHosts(_sql);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _hosts.DisposeAsync();

    [Fact]
    public async Task Help_now_with_the_same_request_id_opens_one_event_and_never_reopens_it()
    {
        using var me = await OnboardedPatientAsync("en");
        var key = Guid.NewGuid();

        var first = await Read(await me.PostAsJsonAsync("/v1/crisis/help", new { requestId = key }));
        first.GetProperty("crisis").GetBoolean().Should().BeTrue();
        first.GetProperty("level").GetInt32().Should().Be(4);
        first.GetProperty("eventOpen").GetBoolean().Should().BeTrue();
        var again = await Read(await me.PostAsJsonAsync("/v1/crisis/help", new { requestId = key }));
        again.GetProperty("eventOpen").GetBoolean().Should().BeTrue();
        again.GetProperty("script").GetString().Should().StartWith("You are not alone");

        var pseudonym = (await Read(await me.GetAsync("/v1/me"))).GetProperty("pseudonymId").GetString();
        await using (var db = _sql.Context(Tamaneen.SharedKernel.StaticCurrentUser.ForStaff(Guid.NewGuid(), Tamaneen.SharedKernel.AppRoles.Supervisor)))
        {
            db.CrisisEvents.Count(e => e.PseudonymId == pseudonym).Should().Be(1);
        }

        // The event is closed; a late resend of the same press (a queued retry) does not open a new one.
        using var console = _hosts.WorkerClient("en", await _hosts.StaffSignInAsync("fahad@staff.tamaneen.dev"));
        var open = await Read(await console.GetAsync("/v1/crisis/events/open"));
        var eventId = open.EnumerateArray().Single(e => e.GetProperty("pseudonymId").GetString() == pseudonym).GetProperty("eventId").GetGuid();
        await Read(await console.PostAsJsonAsync($"/v1/crisis/events/{eventId}/close", new { outcome = "safe_contacted", reason = "Spoke with the patient." }));

        var late = await Read(await me.PostAsJsonAsync("/v1/crisis/help", new { requestId = key }));
        late.GetProperty("crisis").GetBoolean().Should().BeTrue();
        late.GetProperty("eventOpen").GetBoolean().Should().BeFalse();
        (await Read(await me.GetAsync("/v1/crisis/status"))).GetProperty("eventOpen").GetBoolean().Should().BeFalse();

        // A new press is a new request.
        var next = await Read(await me.PostAsJsonAsync("/v1/crisis/help", new { requestId = Guid.NewGuid() }));
        next.GetProperty("eventOpen").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task The_safety_check_escalates_at_the_answer_that_triggers_it()
    {
        using var me = await OnboardedPatientAsync("ar");

        // Each answer is sent as it is given; "no" answers pass and store nothing yet.
        var first = await Read(await me.PostAsJsonAsync("/v1/screening/cssrs", new { answers = new[] { false } }));
        first.GetProperty("crisis").GetBoolean().Should().BeFalse();
        (await Problem(await me.PostAsJsonAsync("/v1/screening/cssrs", new { answers = Array.Empty<bool>() }), 400)).GetProperty("code").GetString().Should().Be("error.validation");
        await Problem(await me.PostAsJsonAsync("/v1/screening/cssrs", new { answers = new bool[7] }), 400);

        // «نعم» on Q2 (High) opens a crisis event at once, before Q3–Q6.
        var yes = await Read(await me.PostAsJsonAsync("/v1/screening/cssrs", new { answers = new[] { false, true } }));
        yes.GetProperty("crisis").GetBoolean().Should().BeTrue();
        yes.GetProperty("level").GetInt32().Should().Be(3);
        yes.GetProperty("script").GetString().Should().Contain("المحادثة متوقفة");
        var status = await Read(await me.GetAsync("/v1/crisis/status"));
        status.GetProperty("eventOpen").GetBoolean().Should().BeTrue();
        status.GetProperty("locked").GetBoolean().Should().BeTrue();

        // The check is not complete, so booking stays closed until it is taken again.
        var pseudonym = (await Read(await me.GetAsync("/v1/me"))).GetProperty("pseudonymId").GetString();
        await using var db = _sql.Context(Tamaneen.SharedKernel.StaticCurrentUser.ForStaff(Guid.NewGuid(), Tamaneen.SharedKernel.AppRoles.Supervisor));
        var patientId = db.UserAccounts.Single(a => a.PseudonymId == pseudonym).Id;
        db.Screenings.Count(x => x.PatientId == patientId).Should().Be(0);
    }

    [Fact]
    public async Task The_safety_plan_keeps_personal_contacts_with_phone_numbers()
    {
        using var me = await OnboardedPatientAsync("en");
        var sections = new Dictionary<string, string> { ["peopleToAsk"] = "My sister", ["reasonToLive"] = "My niece." };

        var invalid = await Problem(await me.PutAsJsonAsync("/v1/safety-plan", new { sections, contacts = new[] { new { name = "Khalid", phone = "12" } } }), 400);
        invalid.GetProperty("errors").TryGetProperty("contacts[0].phone", out _).Should().BeTrue();
        await Problem(await me.PutAsJsonAsync("/v1/safety-plan", new { sections, contacts = new[] { new { name = " ", phone = "+966550001111" } } }), 400);
        await Problem(await me.PutAsJsonAsync("/v1/safety-plan", new { sections, contacts = Enumerable.Range(0, 6).Select(i => new { name = $"P{i}", phone = "+966550001111" }) }), 400);

        var saved = await Read(await me.PutAsJsonAsync("/v1/safety-plan", new { sections, contacts = new[] { new { name = " Khalid ", phone = "+966 55 000 1111" } } }));
        saved.GetProperty("crisis").GetBoolean().Should().BeFalse();
        var plan = await Read(await me.GetAsync("/v1/safety-plan"));
        var contact = plan.GetProperty("contacts").EnumerateArray().Should().ContainSingle().Subject;
        contact.GetProperty("name").GetString().Should().Be("Khalid");
        contact.GetProperty("phone").GetString().Should().Be("+966550001111");
        plan.GetProperty("sections").GetProperty("reasonToLive").GetString().Should().Be("My niece.");
    }

    [Fact]
    public async Task Export_asks_for_the_password_and_deletion_waits_for_an_open_crisis_event()
    {
        using var me = await OnboardedPatientAsync("ar");

        // P11 export (PB-58): the password again.
        (await Problem(await me.PostAsJsonAsync("/v1/me/data-export", new { password = "not-my-password" }), 400)).GetProperty("code").GetString().Should().Be("error.auth.password_mismatch");
        (await Read(await me.PostAsJsonAsync("/v1/me/data-export", new { password = Password }))).GetProperty("exportRequestedAt").ValueKind.Should().NotBe(JsonValueKind.Null);

        // EC-55: no deletion while a crisis event is open, explained in the patient's language.
        await Read(await me.PostAsJsonAsync("/v1/crisis/help", new { requestId = Guid.NewGuid() }));
        var blocked = await Problem(await me.PostAsJsonAsync("/v1/me/delete-account", new { password = Password }), 409);
        blocked.GetProperty("code").GetString().Should().Be("error.privacy.deletion_crisis_open");
        blocked.GetProperty("detail").GetString().Should().Contain("طلب مساعدة مفتوحًا");
        me.DefaultRequestHeaders.AcceptLanguage.Clear();
        me.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        (await Problem(await me.PostAsJsonAsync("/v1/me/delete-account", new { password = Password }), 409)).GetProperty("detail").GetString().Should().Contain("open request for help");
        (await Read(await me.GetAsync("/v1/me"))).GetProperty("pseudonymId").GetString().Should().NotBeNullOrEmpty("the account is still there");
    }

    [Fact]
    public async Task A_trusted_name_needs_a_number_and_a_number_needs_a_name()
    {
        using var me = await OnboardedPatientAsync("en");
        (await Problem(await PutSafetyInfoAsync(me, new { trustedName = "My sister", shareLocation = false }), 400))
            .GetProperty("errors").TryGetProperty("trustedPhone", out _).Should().BeTrue();
        (await Problem(await PutSafetyInfoAsync(me, new { trustedPhone = "0559876543", shareLocation = false }), 400))
            .GetProperty("errors").TryGetProperty("trustedName", out _).Should().BeTrue();
        await Problem(await PutSafetyInfoAsync(me, new { trustedName = new string('a', 101), trustedPhone = "0559876543", shareLocation = false }), 400);
        var ok = await Read(await PutSafetyInfoAsync(me, new { trustedName = new string('a', 100), trustedPhone = "0559876543", shareLocation = false }));
        ok.GetProperty("trustedPhone").GetString().Should().Be("0559876543");
    }

    [Fact]
    public async Task Safety_info_and_profile_saves_need_the_version_that_was_read()
    {
        using var me = await OnboardedPatientAsync("ar");

        // Safety info (P21, P34): no If-Match is 428, an old one is 412, both localized.
        using var first = await me.GetAsync("/v1/me/safety-info");
        var read = first.Headers.ETag!;
        (await Problem(await me.PutAsJsonAsync("/v1/me/safety-info", new { contactPhone = "0551234500", shareLocation = false }), 428))
            .GetProperty("code").GetString().Should().Be("error.settings.version_required");
        var saved = await SendWithIfMatch(me, "/v1/me/safety-info", read, new { contactPhone = "0551234500", shareLocation = false });
        await Read(saved);
        saved.Headers.ETag.Should().NotBeNull().And.NotBe(read, "a save gives a new version");
        var stale = await Problem(await SendWithIfMatch(me, "/v1/me/safety-info", read, new { contactPhone = "0551234511", shareLocation = true }), 412);
        stale.GetProperty("code").GetString().Should().Be("error.settings.version_changed");
        stale.GetProperty("detail").GetString().Should().Contain("جهاز آخر");
        (await Read(await me.GetAsync("/v1/me/safety-info"))).GetProperty("contactPhone").GetString().Should().Be("0551234500", "the stale save changed nothing");
        await Read(await SendWithIfMatch(me, "/v1/me/safety-info", saved.Headers.ETag!, new { contactPhone = "0551234511", shareLocation = false }));

        // Deleting the trusted contact answers with the new version.
        using var deleted = await me.DeleteAsync("/v1/me/safety-info/trusted-contact");
        deleted.Headers.ETag.Should().NotBeNull();
        await Read(await SendWithIfMatch(me, "/v1/me/safety-info", deleted.Headers.ETag!, new { contactPhone = "0551234522", shareLocation = false }));

        // Profile (P31): the ETag of GET /v1/me.
        me.DefaultRequestHeaders.AcceptLanguage.Clear();
        me.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        using var meRead = await me.GetAsync("/v1/me");
        var profileTag = meRead.Headers.ETag!;
        (await Problem(await me.PutAsJsonAsync("/v1/me/profile", new { nickname = "Quiet River", timeZone = "Asia/Riyadh" }), 428))
            .GetProperty("detail").GetString().Should().Contain("Reopen the page");
        var renamed = await SendWithIfMatch(me, "/v1/me/profile", profileTag, new { nickname = "Quiet River", timeZone = "Asia/Riyadh" });
        (await Read(renamed)).GetProperty("nickname").GetString().Should().Be("Quiet River");
        (await Problem(await SendWithIfMatch(me, "/v1/me/profile", profileTag, new { nickname = "Other Name", timeZone = "Asia/Riyadh" }), 412))
            .GetProperty("detail").GetString().Should().Contain("another device");
        await Read(await PutProfileAsync(me, new { nickname = "Other Name", timeZone = "Asia/Riyadh" }));
    }

    private static async Task<HttpResponseMessage> SendWithIfMatch(HttpClient client, string path, System.Net.Http.Headers.EntityTagHeaderValue tag, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body) };
        request.Headers.IfMatch.Add(tag);
        return await client.SendAsync(request);
    }

    private async Task<HttpClient> OnboardedPatientAsync(string language, string path = "direct")
    {
        var email = $"safety-{Guid.NewGuid():N}@example.com";
        using var anon = _hosts.ApiClient(language);
        var eligibility = await Read(await anon.PostAsJsonAsync("/v1/auth/eligibility", new { isAdult = true, cityCode = "RUH" }));
        var challenge = await Read(await anon.PostAsJsonAsync("/v1/auth/signup", new { eligibilityToken = eligibility.GetProperty("eligibilityToken").GetString(), nickname = "Calm Palm", contactType = "email", contact = email, password = Password, language }));
        var verified = await Read(await anon.PostAsJsonAsync("/v1/auth/signup/verify", new { challengeId = challenge.GetProperty("challengeId").GetGuid(), code = _hosts.CodeSentTo(email) }));
        var me = _hosts.ApiClient(language, verified.GetProperty("tokens").GetProperty("accessToken").GetString());
        await Read(await me.PutAsJsonAsync("/v1/me/consents", new { items = new[] { "platform", "ai", "share_with_therapist" }.Select(k => new { key = k, granted = true }) }));
        await Read(await PutSafetyInfoAsync(me, new { shareLocation = false }));
        await Read(await me.PutAsJsonAsync("/v1/me/path", new { path }));
        return me;
    }

    private const string Password = "Quiet-Morning-Walk-77";

    private static JsonElement Prop(JsonElement e, string name) => e.GetProperty(name);
}
