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

    private async Task<HttpClient> OnboardedPatientAsync(string language, string path = "direct")
    {
        var email = $"safety-{Guid.NewGuid():N}@example.com";
        using var anon = _hosts.ApiClient(language);
        var eligibility = await Read(await anon.PostAsJsonAsync("/v1/auth/eligibility", new { isAdult = true, cityCode = "RUH" }));
        var challenge = await Read(await anon.PostAsJsonAsync("/v1/auth/signup", new { eligibilityToken = eligibility.GetProperty("eligibilityToken").GetString(), nickname = "Calm Palm", contactType = "email", contact = email, password = Password, language }));
        var verified = await Read(await anon.PostAsJsonAsync("/v1/auth/signup/verify", new { challengeId = challenge.GetProperty("challengeId").GetGuid(), code = _hosts.CodeSentTo(email) }));
        var me = _hosts.ApiClient(language, verified.GetProperty("tokens").GetProperty("accessToken").GetString());
        await Read(await me.PutAsJsonAsync("/v1/me/consents", new { items = new[] { "platform", "ai", "share_with_therapist" }.Select(k => new { key = k, granted = true }) }));
        await Read(await me.PutAsJsonAsync("/v1/me/safety-info", new { shareLocation = false }));
        await Read(await me.PutAsJsonAsync("/v1/me/path", new { path }));
        return me;
    }

    private const string Password = "Quiet-Morning-Walk-77";

    private static JsonElement Prop(JsonElement e, string name) => e.GetProperty(name);
}
