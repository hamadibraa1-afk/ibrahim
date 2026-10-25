using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Tamaneen.IntegrationTests.JourneyHosts;

namespace Tamaneen.IntegrationTests;

/// <summary>
/// Server side of the patient outbox (P66, EC-01, EC-07): a write sent again with the same Idempotency-Key is
/// stored once and answered with the first result, its text reaches the crisis gate once, a failed first try
/// frees the key, and keys belong to one patient.
/// </summary>
[Collection(SqlServerGroup.Name)]
public sealed class OutboxIdempotencyTests : IAsyncLifetime
{
    private const string Password = "Quiet-Morning-Walk-77";

    private readonly SqlServerFixture _sql;
    private JourneyHosts _hosts = null!;

    public OutboxIdempotencyTests(SqlServerFixture sql) => _sql = sql;

    public Task InitializeAsync()
    {
        _hosts = new JourneyHosts(_sql);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _hosts.DisposeAsync();

    [Fact]
    public async Task A_resent_mood_check_in_is_stored_once_and_answered_with_the_first_result()
    {
        using var me = await OnboardedPatientAsync("en");
        var key = Guid.NewGuid();

        using var firstResponse = await SendAsync(me, HttpMethod.Post, "/v1/mood", key, new { score = 6, tags = new[] { "calm" }, note = "A quiet day." });
        firstResponse.Headers.Contains("Idempotent-Replayed").Should().BeFalse();
        var first = await Read(firstResponse);

        // The response was lost; the outbox sends the same key again, even with a body it rebuilt.
        using var againResponse = await SendAsync(me, HttpMethod.Post, "/v1/mood", key, new { score = 6, tags = new[] { "calm" }, note = "A quiet day." });
        againResponse.Headers.GetValues("Idempotent-Replayed").Should().ContainSingle().Which.Should().Be("true");
        var again = await Read(againResponse);
        again.GetProperty("entry").GetProperty("id").GetGuid().Should().Be(first.GetProperty("entry").GetProperty("id").GetGuid());
        again.GetProperty("entry").GetProperty("score").GetInt32().Should().Be(6);
        again.GetProperty("crisis").GetBoolean().Should().BeFalse();

        (await Read(await me.GetAsync("/v1/mood?days=1"))).GetArrayLength().Should().Be(1);

        // A new entry is a new key; without a key nothing changes for older clients.
        await Read(await SendAsync(me, HttpMethod.Post, "/v1/mood", Guid.NewGuid(), new { score = 7 }));
        await Read(await me.PostAsJsonAsync("/v1/mood", new { score = 8 }));
        (await Read(await me.GetAsync("/v1/mood?days=1"))).GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task Two_copies_sent_at_once_save_one_entry()
    {
        using var me = await OnboardedPatientAsync("en");
        var key = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => SendAsync(me, HttpMethod.Post, "/v1/mood", key, new { score = 4 })));
        try
        {
            responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Conflict);
            foreach (var conflict in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
            {
                (await Problem(conflict, 409)).GetProperty("code").GetString().Should().Be("error.idempotency.in_progress");
                conflict.Headers.RetryAfter.Should().NotBeNull();
            }
        }
        finally
        {
            foreach (var r in responses)
            {
                r.Dispose();
            }
        }

        // The retry after "in progress" gets the stored result.
        await Read(await SendAsync(me, HttpMethod.Post, "/v1/mood", key, new { score = 4 }));
        (await Read(await me.GetAsync("/v1/mood?days=1"))).GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task A_queued_note_goes_through_the_crisis_gate_once_and_its_replay_still_opens_the_crisis_screen()
    {
        using var me = await OnboardedPatientAsync("ar");
        var key = Guid.NewGuid();
        var body = new { score = 1, tags = new[] { "tired" }, note = "ما عاد أتحمل، أبي أموت الليلة" };

        var first = await Read(await SendAsync(me, HttpMethod.Post, "/v1/mood", key, body));
        first.GetProperty("crisis").GetBoolean().Should().BeTrue();
        var again = await Read(await SendAsync(me, HttpMethod.Post, "/v1/mood", key, body));
        again.GetProperty("crisis").GetBoolean().Should().BeTrue();
        again.GetProperty("script").GetString().Should().Be(first.GetProperty("script").GetString());

        var pseudonym = (await Read(await me.GetAsync("/v1/me"))).GetProperty("pseudonymId").GetString();
        await using var db = _sql.Context(Tamaneen.SharedKernel.StaticCurrentUser.ForStaff(Guid.NewGuid(), Tamaneen.SharedKernel.AppRoles.Supervisor));
        db.CrisisEvents.Count(e => e.PseudonymId == pseudonym).Should().Be(1);
        var patientId = db.UserAccounts.Single(a => a.PseudonymId == pseudonym).Id;
        db.MoodEntries.Count(m => m.PatientId == patientId).Should().Be(1);
    }

    [Fact]
    public async Task A_safety_plan_edit_is_replayed_with_its_first_result_and_the_stored_response_is_encrypted()
    {
        using var me = await OnboardedPatientAsync("en");
        var key = Guid.NewGuid();
        var plan = new { sections = new Dictionary<string, string> { ["warningSigns"] = "Not sleeping", ["reasonToLive"] = "My niece." }, contacts = new[] { new { name = "Sister", phone = "+966550001111" } } };

        var first = await Read(await SendAsync(me, HttpMethod.Put, "/v1/safety-plan", key, plan));
        first.GetProperty("sections").GetProperty("warningSigns").GetString().Should().Be("Not sleeping");

        // A newer edit with its own key, then a late resend of the first: the late one does not overwrite the newer plan.
        await Read(await SendAsync(me, HttpMethod.Put, "/v1/safety-plan", Guid.NewGuid(), new { sections = new Dictionary<string, string> { ["warningSigns"] = "Staying in bed" }, contacts = Array.Empty<object>() }));
        var late = await Read(await SendAsync(me, HttpMethod.Put, "/v1/safety-plan", key, plan));
        late.GetProperty("sections").GetProperty("warningSigns").GetString().Should().Be("Not sleeping", "the replay is the first result");
        (await Read(await me.GetAsync("/v1/safety-plan"))).GetProperty("sections").GetProperty("warningSigns").GetString().Should().Be("Staying in bed");

        await using var db = _sql.Context(Tamaneen.SharedKernel.StaticCurrentUser.ForStaff(Guid.NewGuid(), Tamaneen.SharedKernel.AppRoles.Supervisor));
        var pseudonym = (await Read(await me.GetAsync("/v1/me"))).GetProperty("pseudonymId").GetString();
        var patientId = db.UserAccounts.Single(a => a.PseudonymId == pseudonym).Id;
        await using var own = _sql.Context(Tamaneen.SharedKernel.StaticCurrentUser.ForPatient(patientId));
        var record = own.IdempotencyRecords.Single(r => r.Key == key);
        record.CompletedAtUtc.Should().NotBeNull();
        System.Text.Encoding.UTF8.GetString(record.ResponseCipher!).Should().NotContain("Not sleeping");
    }

    [Fact]
    public async Task A_failed_first_try_frees_the_key_and_a_key_belongs_to_one_request_and_one_patient()
    {
        using var me = await OnboardedPatientAsync("ar");
        var key = Guid.NewGuid();

        // Refused (too long): nothing stored, so the corrected retry with the same key runs.
        await Problem(await SendAsync(me, HttpMethod.Post, "/v1/mood", key, new { score = 5, note = new string('a', 1001) }), 400);
        (await Read(await SendAsync(me, HttpMethod.Post, "/v1/mood", key, new { score = 5 }))).GetProperty("entry").GetProperty("score").GetInt32().Should().Be(5);

        // The same key on another endpoint is refused, in the patient's language.
        var reused = await Problem(await SendAsync(me, HttpMethod.Put, "/v1/safety-plan", key, new { sections = new Dictionary<string, string>() }), 422);
        reused.GetProperty("code").GetString().Should().Be("error.idempotency.key_reused");
        reused.GetProperty("detail").GetString().Should().Contain("لم نرسله");

        var invalid = await Problem(await SendAsync(me, HttpMethod.Post, "/v1/mood", null, new { score = 5 }, rawKey: "not-a-guid"), 400);
        invalid.GetProperty("code").GetString().Should().Be("error.idempotency.invalid_key");

        // Another patient's write with the same key is their own: it runs and is never answered with someone else's result.
        using var other = await OnboardedPatientAsync("en");
        var theirs = await Read(await SendAsync(other, HttpMethod.Post, "/v1/mood", key, new { score = 9 }));
        theirs.GetProperty("entry").GetProperty("score").GetInt32().Should().Be(9);
        (await Read(await other.GetAsync("/v1/mood?days=1"))).GetArrayLength().Should().Be(1);
        (await Read(await me.GetAsync("/v1/mood?days=1"))).GetArrayLength().Should().Be(1);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, Guid? key, object body, string? rawKey = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", rawKey ?? key!.Value.ToString());
        return client.SendAsync(request);
    }

    private async Task<HttpClient> OnboardedPatientAsync(string language)
    {
        var email = $"outbox-{Guid.NewGuid():N}@example.com";
        using var anon = _hosts.ApiClient(language);
        var eligibility = await Read(await anon.PostAsJsonAsync("/v1/auth/eligibility", new { isAdult = true, cityCode = "RUH" }));
        var challenge = await Read(await anon.PostAsJsonAsync("/v1/auth/signup", new { eligibilityToken = eligibility.GetProperty("eligibilityToken").GetString(), nickname = "Calm Palm", contactType = "email", contact = email, password = Password, language }));
        var verified = await Read(await anon.PostAsJsonAsync("/v1/auth/signup/verify", new { challengeId = challenge.GetProperty("challengeId").GetGuid(), code = _hosts.CodeSentTo(email) }));
        var me = _hosts.ApiClient(language, verified.GetProperty("tokens").GetProperty("accessToken").GetString());
        await Read(await me.PutAsJsonAsync("/v1/me/consents", new { items = new[] { "platform", "ai", "share_with_therapist" }.Select(k => new { key = k, granted = true }) }));
        await Read(await me.PutAsJsonAsync("/v1/me/safety-info", new { shareLocation = false }));
        await Read(await me.PutAsJsonAsync("/v1/me/path", new { path = "direct" }));
        return me;
    }
}
