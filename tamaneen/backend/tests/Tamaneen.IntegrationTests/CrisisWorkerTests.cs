using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tamaneen.Crisis;
using Tamaneen.Crisis.Worker.Escalation;
using Tamaneen.Crisis.Worker.Paging;
using Tamaneen.Hosting;
using Tamaneen.SharedKernel;

namespace Tamaneen.IntegrationTests;

[Collection(SqlServerGroup.Name)]
public sealed class CrisisWorkerTests : IAsyncLifetime
{
    private readonly SqlServerFixture _sql;
    private WorkerFactory _worker = null!;

    public CrisisWorkerTests(SqlServerFixture sql) => _sql = sql;

    public Task InitializeAsync()
    {
        _worker = new WorkerFactory(_sql);
        _worker.Pager.Answer = HttpStatusCode.Accepted;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _worker.DisposeAsync();

    private async Task<JsonElement> EvaluateAsync(Guid patientId, string text, string language = "ar", string source = "chat")
    {
        using var client = _worker.ServiceClient();
        using var response = await client.PostAsJsonAsync("/internal/v1/gate/evaluate", new { patientId, language, text, source });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task RunAsServiceAsync<T>(Func<T, Task> action)
        where T : notnull
    {
        await using var scope = _worker.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestCurrentUser>().ActAsService(_worker.ServiceUserId, AppRoles.CrisisService);
        await action(scope.ServiceProvider.GetRequiredService<T>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task Evaluate_requires_the_service_key(string? key)
    {
        using var client = key is null ? _worker.CreateClient() : _worker.ServiceClient(key);
        using var response = await client.PostAsJsonAsync("/internal/v1/gate/evaluate", new { patientId = Guid.NewGuid(), language = "ar", text = "x", source = "chat" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Evaluate_validates_its_input()
    {
        using var client = _worker.ServiceClient();
        using var response = await client.PostAsJsonAsync("/internal/v1/gate/evaluate", new { patientId = Guid.NewGuid(), language = "ar", text = new string('a', 9000), source = "email" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").TryGetProperty("source", out _).Should().BeTrue();
        problem.GetProperty("errors").TryGetProperty("text", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Ordinary_messages_pass_and_create_nothing()
    {
        var patient = await _sql.CreatePatientAsync("هيا");
        var result = await EvaluateAsync(patient.Id, "الجلسة ساعدتني كثير");
        result.GetProperty("decision").GetString().Should().Be("pass");
        await using var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor));
        (await db.CrisisEvents.AnyAsync(e => e.PatientId == patient.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Level_4_locks_the_chat_alerts_each_responder_in_their_language_and_starts_a_2_minute_timer()
    {
        var supervisorAr = await _sql.CreateStaffAsync(AppRoles.Supervisor);
        var supervisorEn = await _sql.CreateStaffAsync(AppRoles.Supervisor);
        var (connAr, inboxAr) = await _worker.ConnectConsoleAsync(supervisorAr.Id, AppRoles.Supervisor, "ar");
        var (connEn, inboxEn) = await _worker.ConnectConsoleAsync(supervisorEn.Id, AppRoles.Supervisor, "en");
        await using var _ = connAr;
        await using var __ = connEn;
        var patient = await _sql.CreatePatientAsync("نورة", "ar");

        var result = await EvaluateAsync(patient.Id, "خلاص سأنتحر الليلة");

        result.GetProperty("decision").GetString().Should().Be("blocked");
        result.GetProperty("level").GetInt32().Should().Be(4);
        result.GetProperty("chatLocked").GetBoolean().Should().BeTrue();
        result.GetProperty("dir").GetString().Should().Be("rtl");
        result.GetProperty("script").GetString().Should().StartWith("أنت لست وحدك").And.Contain("911").And.Contain("920033360");
        var eventId = result.GetProperty("crisisEventId").GetGuid();

        var ar = await inboxAr.WaitForAsync(m => m.EventId == eventId);
        ar.Title.Should().Be($"خطر حرج · {patient.PseudonymId}");
        ar.Body.Should().Contain("2 دقيقة").And.Contain("العربية");
        ar.Categories.Should().Contain("نية انتحار");
        ar.Dir.Should().Be("rtl");
        var en = await inboxEn.WaitForAsync(m => m.EventId == eventId);
        en.Title.Should().Be($"Critical risk · {patient.PseudonymId}");
        en.Body.Should().Contain("2 minutes").And.Contain("Arabic");
        en.Categories.Should().Contain("Suicidal intent");
        en.Dir.Should().Be("ltr");
        JsonSerializer.Serialize(en).Should().NotContain("سأنتحر", "patient words never leave the gate");

        await using var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor));
        var evt = await db.CrisisEvents.SingleAsync(e => e.Id == eventId);
        evt.Level.Should().Be(RiskLevel.Critical);
        evt.AckDeadlineUtc.Should().Be(_worker.Clock.UtcNow.AddMinutes(2));
        (await db.ChatLocks.SingleAsync(l => l.CrisisEventId == eventId)).ReleasedAtUtc.Should().BeNull();
        (await db.PageRequests.SingleAsync(p => p.CrisisEventId == eventId)).Tier.Should().Be(0);
        (await db.AuditEntries.CountAsync(a => a.EntityId == eventId.ToString() && a.Action == "crisis.opened")).Should().Be(1);
    }

    [Fact]
    public async Task While_locked_further_messages_are_blocked_without_new_events_or_pages()
    {
        var patient = await _sql.CreatePatientAsync("فهد", "en");
        var first = await EvaluateAsync(patient.Id, "I want to kill myself", "en");
        var second = await EvaluateAsync(patient.Id, "just kidding I'm fine", "en");

        second.GetProperty("decision").GetString().Should().Be("blocked");
        second.GetProperty("crisisEventId").GetGuid().Should().Be(first.GetProperty("crisisEventId").GetGuid());
        second.GetProperty("script").GetString().Should().StartWith("You are not alone");
        await using var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor));
        (await db.CrisisEvents.CountAsync(e => e.PatientId == patient.Id)).Should().Be(1);
        (await db.PageRequests.CountAsync(p => p.PseudonymId == patient.PseudonymId)).Should().Be(1);
    }

    [Fact]
    public async Task First_acknowledgement_wins_and_a_second_gets_a_localized_conflict()
    {
        var patient = await _sql.CreatePatientAsync("ريم");
        var eventId = (await EvaluateAsync(patient.Id, "ابي اموت")).GetProperty("crisisEventId").GetGuid();
        var a = await _sql.CreateStaffAsync(AppRoles.Supervisor);
        var b = await _sql.CreateStaffAsync(AppRoles.Supervisor);

        using var clientA = _worker.StaffClient(a.Id, AppRoles.Supervisor, "en");
        using var ok = await clientA.PostAsync($"/v1/crisis/events/{eventId}/acknowledge", null);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString().Should().StartWith("Acknowledged");

        using var clientB = _worker.StaffClient(b.Id, AppRoles.Supervisor, "ar");
        using var conflict = await clientB.PostAsync($"/v1/crisis/events/{eventId}/acknowledge", null);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString().Should().Be("تولّى شخص آخر هذه الحالة من قبل.");

        await using var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor));
        var evt = await db.CrisisEvents.SingleAsync(e => e.Id == eventId);
        evt.AcknowledgedByUserId.Should().Be(a.Id);
        evt.ContactDeadlineUtc.Should().Be(_worker.Clock.UtcNow.AddMinutes(60));
    }

    [Fact]
    public async Task A_therapist_who_is_not_assigned_cannot_see_or_take_the_event()
    {
        var patient = await _sql.CreatePatientAsync("سارة");
        var eventId = (await EvaluateAsync(patient.Id, "سأنتحر")).GetProperty("crisisEventId").GetGuid();
        var therapist = await _sql.CreateStaffAsync(AppRoles.Therapist);
        using var client = _worker.StaffClient(therapist.Id, AppRoles.Therapist, "en");
        (await client.PostAsync($"/v1/crisis/events/{eventId}/acknowledge", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var open = await client.GetFromJsonAsync<JsonElement>("/v1/crisis/events/open");
        open.EnumerateArray().Select(e => e.GetProperty("eventId").GetGuid()).Should().NotContain(eventId);

        await _sql.AssignAsync(patient.Id, therapist.Id);
        (await client.PostAsync($"/v1/crisis/events/{eventId}/acknowledge", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unacknowledged_critical_event_escalates_after_2_minutes_and_pages_the_next_tier()
    {
        var supervisor = await _sql.CreateStaffAsync(AppRoles.Supervisor);
        var (conn, inbox) = await _worker.ConnectConsoleAsync(supervisor.Id, AppRoles.Supervisor, "ar");
        await using var _ = conn;
        var patient = await _sql.CreatePatientAsync("خالد");
        var eventId = (await EvaluateAsync(patient.Id, "بلعت العلبة كلها")).GetProperty("crisisEventId").GetGuid();

        _worker.Clock.Advance(TimeSpan.FromSeconds(119));
        await RunAsServiceAsync<EscalationService>(async s => await s.RunOnceAsync(default));
        await using (var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor)))
        {
            (await db.CrisisEvents.SingleAsync(e => e.Id == eventId)).EscalationTier.Should().Be(0, "still inside the 2-minute window");
        }

        _worker.Clock.Advance(TimeSpan.FromSeconds(2));
        await RunAsServiceAsync<EscalationService>(async s => await s.RunOnceAsync(default));

        await using (var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor)))
        {
            var evt = await db.CrisisEvents.SingleAsync(e => e.Id == eventId);
            evt.EscalationTier.Should().Be(1);
            evt.AckDeadlineUtc.Should().Be(_worker.Clock.UtcNow.AddMinutes(2));
            (await db.PageRequests.Where(p => p.CrisisEventId == eventId).OrderBy(p => p.Tier).Select(p => (int)p.Tier).ToListAsync()).Should().Equal(0, 1);
            (await db.AuditEntries.CountAsync(a => a.EntityId == eventId.ToString() && a.Action == "crisis.escalated")).Should().Be(1);
        }

        var escalated = await inbox.WaitForAsync(m => m.EventId == eventId && m.Kind == "escalated");
        escalated.Title.Should().Contain("المناوب الاحتياطي");
    }

    [Fact]
    public async Task Closing_needs_a_supervisor_and_a_reason_and_reopens_the_chat()
    {
        var patient = await _sql.CreatePatientAsync("منى");
        var eventId = (await EvaluateAsync(patient.Id, "سأنتحر")).GetProperty("crisisEventId").GetGuid();
        var therapist = await _sql.CreateStaffAsync(AppRoles.Therapist);
        await _sql.AssignAsync(patient.Id, therapist.Id);
        var supervisor = await _sql.CreateStaffAsync(AppRoles.Supervisor);

        using var asTherapist = _worker.StaffClient(therapist.Id, AppRoles.Therapist, "en");
        (await asTherapist.PostAsJsonAsync($"/v1/crisis/events/{eventId}/close", new { reason = "ok" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var asSupervisor = _worker.StaffClient(supervisor.Id, AppRoles.Supervisor, "en");
        using var noReason = await asSupervisor.PostAsJsonAsync($"/v1/crisis/events/{eventId}/close", new { reason = "" });
        noReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noReason.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString().Should().Be("Please write a reason.");

        // P38 close steps: a follow-up call in the next 14 days and a support person code, nothing else.
        (await asSupervisor.PostAsJsonAsync($"/v1/crisis/events/{eventId}/close", new { reason = "ok", supportPerson = "sister Huda" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await asSupervisor.PostAsJsonAsync($"/v1/crisis/events/{eventId}/close", new { reason = "ok", followUpCallAt = DateTimeOffset.UtcNow.AddDays(20) })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var callAt = DateTimeOffset.UtcNow.AddDays(1);
        (await asSupervisor.PostAsJsonAsync($"/v1/crisis/events/{eventId}/close", new { reason = "Reached by phone, safe with family, safety plan reviewed", followUpCallAt = callAt, supportPerson = "family" })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await EvaluateAsync(patient.Id, "شكرًا، أحس أحسن")).GetProperty("decision").GetString().Should().Be("pass");
        await using var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor));
        (await db.ChatLocks.SingleAsync(l => l.CrisisEventId == eventId)).ReleasedByUserId.Should().Be(supervisor.Id);
        var closed = await db.CrisisEvents.SingleAsync(e => e.Id == eventId);
        closed.SupportPerson.Should().Be("family");
        closed.FollowUpCallAtUtc.Should().BeCloseTo(callAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Pages_are_sent_signed_without_patient_content_and_retried_on_failure()
    {
        var patient = await _sql.CreatePatientAsync("علي");
        var eventId = (await EvaluateAsync(patient.Id, "ودعت أهلي وهذي آخر رسالة")).GetProperty("crisisEventId").GetGuid();

        _worker.Pager.Answer = HttpStatusCode.ServiceUnavailable;
        await RunAsServiceAsync<PageDispatcher>(async d => await d.RunOnceAsync(default));
        await using (var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor)))
        {
            var page = await db.PageRequests.SingleAsync(p => p.CrisisEventId == eventId);
            page.SentAtUtc.Should().BeNull();
            page.Attempts.Should().Be(1);
            page.NextAttemptAtUtc.Should().Be(_worker.Clock.UtcNow.AddSeconds(5));
        }

        _worker.Pager.Answer = HttpStatusCode.Accepted;
        _worker.Clock.Advance(TimeSpan.FromSeconds(6));
        await RunAsServiceAsync<PageDispatcher>(async d => await d.RunOnceAsync(default));

        var (request, body) = _worker.Pager.Requests.Last(r => r.Body.Contains(eventId.ToString(), StringComparison.Ordinal));
        var timestamp = request.Headers.GetValues(WebhookPagerChannel.TimestampHeader).Single();
        request.Headers.GetValues(WebhookPagerChannel.SignatureHeader).Single().Should().Be("sha256=" + WebhookPagerChannel.Sign(_worker.PagingSecret, timestamp, body));
        body.Should().Contain(patient.PseudonymId).And.Contain("https://staff.tamaneen.test/crisis/");
        body.Should().NotContain("رسالة").And.NotContain("علي");
        await using (var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor)))
        {
            (await db.PageRequests.SingleAsync(p => p.CrisisEventId == eventId)).SentAtUtc.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task An_unknown_patient_still_gets_crisis_resources_fail_closed()
    {
        var result = await EvaluateAsync(Guid.NewGuid(), "سأنتحر", "en");
        result.GetProperty("decision").GetString().Should().Be("blocked");
        result.GetProperty("script").GetString().Should().StartWith("We're having a technical problem").And.Contain("911");
    }

    [Fact]
    public async Task Questionnaire_answers_open_an_event_through_the_endpoint()
    {
        var patient = await _sql.CreatePatientAsync("ليلى");
        using var client = _worker.ServiceClient();
        using var response = await client.PostAsJsonAsync("/internal/v1/gate/evaluate", new
        {
            patientId = patient.Id,
            language = "ar",
            answers = new { instrument = "PHQ-9", scores = new Dictionary<int, int> { [9] = 2 } },
            source = "questionnaire",
        });
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("level").GetInt32().Should().Be(3);
        result.GetProperty("ruleIds")[0].GetString().Should().Be("Q-PHQ9-I9");
    }

    [Fact]
    public async Task Readiness_reports_the_database()
    {
        using var client = _worker.CreateClient();
        (await client.GetStringAsync("/health/ready")).Should().Be("Healthy");
    }
}
