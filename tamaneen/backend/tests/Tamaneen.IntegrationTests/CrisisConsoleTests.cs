using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tamaneen.Crisis;
using Tamaneen.Identity;
using Tamaneen.SharedKernel;

namespace Tamaneen.IntegrationTests;

/// <summary>S04 console rules on the worker: one owner, calls and contact, handover with acceptance, co-signed unreachable close.</summary>
[Collection(SqlServerGroup.Name)]
public sealed class CrisisConsoleTests : IAsyncLifetime
{
    private readonly SqlServerFixture _sql;
    private WorkerFactory _worker = null!;

    public CrisisConsoleTests(SqlServerFixture sql) => _sql = sql;

    public Task InitializeAsync()
    {
        _worker = new WorkerFactory(_sql);
        _worker.Pager.Answer = HttpStatusCode.Accepted;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _worker.DisposeAsync();

    private async Task<Guid> OpenCriticalAsync(Guid patientId)
    {
        using var client = _worker.ServiceClient();
        using var response = await client.PostAsJsonAsync("/internal/v1/gate/evaluate", new { patientId, language = "ar", text = "بلعت العلبة كلها", source = "chat" });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("crisisEventId").GetGuid();
    }

    private async Task<UserAccount> StaffWithProfileAsync(string role, string nameEn)
    {
        var account = await _sql.CreateStaffAsync(role);
        await using var db = _sql.Context(StaticCurrentUser.Anonymous);
        db.StaffProfiles.Add(StaffProfile.Create(account.Id, nameEn, nameEn));
        await db.SaveChangesAsync();
        return account;
    }

    private static async Task<JsonElement> Ok(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response, HttpStatusCode status)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("code").GetString();
    }

    [Fact]
    public async Task Once_taken_others_read_only_but_the_patients_therapist_can_still_refer_to_the_on_call()
    {
        var patient = await _sql.CreatePatientAsync("هدى");
        var eventId = await OpenCriticalAsync(patient.Id);
        var therapist = await _sql.CreateStaffAsync(AppRoles.Therapist);
        await _sql.AssignAsync(patient.Id, therapist.Id);
        var supervisor = await _sql.CreateStaffAsync(AppRoles.Supervisor);
        using var asTherapist = _worker.StaffClient(therapist.Id, AppRoles.Therapist, "en");
        using var asSupervisor = _worker.StaffClient(supervisor.Id, AppRoles.Supervisor, "en");

        // Unacknowledged: the therapist refers with a note and the event stays open, nobody's.
        (await CodeOf(await asTherapist.PostAsJsonAsync($"/v1/crisis/events/{eventId}/actions", new { kind = "refer" }), HttpStatusCode.BadRequest)).Should().Be(ErrorCodes.Validation);
        var referred = await Ok(await asTherapist.PostAsJsonAsync($"/v1/crisis/events/{eventId}/actions", new { kind = "refer", text = "Please call her, she knows me as Dr. Sara" }));
        referred.GetProperty("status").GetString().Should().Be("Open");
        referred.GetProperty("timeline").EnumerateArray().Should().Contain(t => t.GetProperty("kind").GetString() == "refer");
        referred.GetProperty("timeline").EnumerateArray().Last().GetProperty("kind").GetString().Should().Be("next_escalation", "the scheduled escalation is the last, future step");

        await Ok(await asSupervisor.PostAsync($"/v1/crisis/events/{eventId}/acknowledge", null));
        var readOnly = await Ok(await asTherapist.GetAsync($"/v1/crisis/events/{eventId}"));
        readOnly.GetProperty("canAct").GetBoolean().Should().BeFalse();
        readOnly.GetProperty("isOwner").GetBoolean().Should().BeFalse();
        (await CodeOf(await asTherapist.PostAsJsonAsync($"/v1/crisis/events/{eventId}/actions", new { kind = "note", text = "x" }), HttpStatusCode.Forbidden)).Should().Be(ErrorCodes.CrisisNotOwner);
        (await CodeOf(await asTherapist.PostAsJsonAsync($"/v1/crisis/events/{eventId}/actions", new { kind = "local_emergency", text = "x" }), HttpStatusCode.Forbidden)).Should().Be(ErrorCodes.CrisisNotOwner);
        await Ok(await asTherapist.PostAsJsonAsync($"/v1/crisis/events/{eventId}/actions", new { kind = "refer", text = "Still reachable on the evening number" }));

        var owner = await Ok(await asSupervisor.GetAsync($"/v1/crisis/events/{eventId}"));
        owner.GetProperty("canAct").GetBoolean().Should().BeTrue();
        owner.GetProperty("isOwner").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Calls_are_logged_with_their_outcome_and_contact_moves_the_event_to_in_contact()
    {
        var patient = await _sql.CreatePatientAsync("ليلى");
        var eventId = await OpenCriticalAsync(patient.Id);
        var supervisor = await _sql.CreateStaffAsync(AppRoles.Supervisor);
        using var client = _worker.StaffClient(supervisor.Id, AppRoles.Supervisor, "en");
        await Ok(await client.PostAsync($"/v1/crisis/events/{eventId}/acknowledge", null));

        var detail = await Ok(await client.GetAsync($"/v1/crisis/events/{eventId}"));
        var number = detail.GetProperty("emergencyNumber").GetString()!;
        detail.GetProperty("emergencyScript").GetString().Should().Contain(number).And.Contain("No location is on file");

        for (var i = 0; i < 3; i++)
        {
            await Ok(await client.PostAsJsonAsync($"/v1/crisis/events/{eventId}/actions", new { kind = "call_attempt" }));
            detail = await Ok(await client.PostAsJsonAsync($"/v1/crisis/events/{eventId}/actions", new { kind = "call_outcome", outcome = "no_answer" }));
        }

        (await CodeOf(await client.PostAsJsonAsync($"/v1/crisis/events/{eventId}/actions", new { kind = "call_outcome", outcome = "maybe" }), HttpStatusCode.BadRequest)).Should().Be(ErrorCodes.Validation);
        detail.GetProperty("recentCallAttempts").GetInt32().Should().Be(3);
        detail.GetProperty("callAdvice").GetString().Should().Be("unreachable", "three unanswered calls to a critical patient with no location on file (EC-33)");
        detail.GetProperty("timeline").EnumerateArray().Select(t => t.GetProperty("text").GetString()).Should().Contain("Call outcome: no answer");

        (await CodeOf(await client.PostAsJsonAsync($"/v1/crisis/events/{eventId}/actions", new { kind = "contacted" }), HttpStatusCode.BadRequest)).Should().Be(ErrorCodes.Validation);
        detail = await Ok(await client.PostAsJsonAsync($"/v1/crisis/events/{eventId}/actions", new { kind = "contacted", text = "Called back, safe at home with her mother" }));
        detail.GetProperty("status").GetString().Should().Be("InContact");
        detail.GetProperty("callAdvice").ValueKind.Should().Be(JsonValueKind.Null);
        var open = await Ok(await client.GetAsync("/v1/crisis/events/open"));
        open.EnumerateArray().Single(e => e.GetProperty("eventId").GetGuid() == eventId).GetProperty("status").GetString().Should().Be("InContact");
    }

    [Fact]
    public async Task A_handover_goes_to_a_chosen_colleague_and_moves_ownership_only_when_accepted()
    {
        var patient = await _sql.CreatePatientAsync("مريم");
        var eventId = await OpenCriticalAsync(patient.Id);
        var from = await StaffWithProfileAsync(AppRoles.Supervisor, "Dr. From");
        var to = await StaffWithProfileAsync(AppRoles.Supervisor, "Dr. To");
        using var asFrom = _worker.StaffClient(from.Id, AppRoles.Supervisor, "en");
        using var asTo = _worker.StaffClient(to.Id, AppRoles.Supervisor, "en");
        await Ok(await asFrom.PostAsync($"/v1/crisis/events/{eventId}/acknowledge", null));

        var responders = await Ok(await asFrom.GetAsync("/v1/crisis/responders"));
        responders.EnumerateArray().Should().Contain(r => r.GetProperty("userId").GetGuid() == to.Id && r.GetProperty("displayName").GetString() == "Dr. To");
        responders.EnumerateArray().Should().NotContain(r => r.GetProperty("userId").GetGuid() == from.Id);

        (await CodeOf(await asFrom.PostAsJsonAsync($"/v1/crisis/events/{eventId}/handover", new { toUserId = Guid.NewGuid(), note = "x" }), HttpStatusCode.BadRequest)).Should().Be(ErrorCodes.Validation);
        var offered = await Ok(await asFrom.PostAsJsonAsync($"/v1/crisis/events/{eventId}/handover", new { toUserId = to.Id, note = "Shift ends; she prefers a call after 9" }));
        offered.GetProperty("acknowledgedBy").GetString().Should().Be("Dr. From", "ownership stays until the colleague accepts");
        offered.GetProperty("pendingHandover").GetProperty("toName").GetString().Should().Be("Dr. To");
        offered.GetProperty("pendingHandover").GetProperty("forMe").GetBoolean().Should().BeFalse();

        var forTo = await Ok(await asTo.GetAsync($"/v1/crisis/events/{eventId}"));
        forTo.GetProperty("pendingHandover").GetProperty("forMe").GetBoolean().Should().BeTrue();
        (await CodeOf(await asFrom.PostAsync($"/v1/crisis/events/{eventId}/handover/accept", null), HttpStatusCode.Conflict)).Should().Be(ErrorCodes.CrisisNothingPending);

        _worker.Clock.Advance(TimeSpan.FromSeconds(20));
        var accepted = await Ok(await asTo.PostAsync($"/v1/crisis/events/{eventId}/handover/accept", null));
        accepted.GetProperty("acknowledgedBy").GetString().Should().Be("Dr. To");
        accepted.GetProperty("isOwner").GetBoolean().Should().BeTrue();
        accepted.GetProperty("pendingHandover").ValueKind.Should().Be(JsonValueKind.Null);
        accepted.GetProperty("timeline").EnumerateArray().Select(t => t.GetProperty("kind").GetString()).Should().ContainInOrder("acknowledged", "handover_requested", "handover_accepted");
    }

    [Fact]
    public async Task Unreachable_needs_the_clinical_leads_co_sign_and_leaves_a_72_hour_review()
    {
        var patient = await _sql.CreatePatientAsync("سلمى");
        var eventId = await OpenCriticalAsync(patient.Id);
        var supervisor = await _sql.CreateStaffAsync(AppRoles.Supervisor);
        var lead = await _sql.CreateStaffAsync(AppRoles.ClinicalLead);
        using var asSupervisor = _worker.StaffClient(supervisor.Id, AppRoles.Supervisor, "en");
        using var asLead = _worker.StaffClient(lead.Id, AppRoles.ClinicalLead, "ar");

        var requested = await Ok(await asSupervisor.PostAsJsonAsync($"/v1/crisis/events/{eventId}/close", new { outcome = "unreachable_referred", reason = "Three calls, no answer, no location" }));
        requested.GetProperty("status").GetString().Should().Be("Open");
        (await CodeOf(await asSupervisor.PostAsync($"/v1/crisis/events/{eventId}/close/cosign", null), HttpStatusCode.Forbidden)).Should().Be(ErrorCodes.CrisisCloseNotAllowed);

        var forLead = await Ok(await asLead.GetAsync($"/v1/crisis/events/{eventId}"));
        forLead.GetProperty("canCoSign").GetBoolean().Should().BeTrue();
        forLead.GetProperty("canClose").GetBoolean().Should().BeFalse("a close already waits for the co-sign");
        forLead.GetProperty("pendingClose").GetProperty("reason").GetString().Should().Be("Three calls, no answer, no location");

        (await Ok(await asLead.PostAsync($"/v1/crisis/events/{eventId}/close/cosign", null))).GetProperty("status").GetString().Should().Be("Closed");
        await using var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor));
        var evt = await db.CrisisEvents.SingleAsync(e => e.Id == eventId);
        evt.CloseOutcome.Should().Be("unreachable_referred");
        evt.CoSignedByUserId.Should().Be(lead.Id);
        evt.ReviewDueAtUtc.Should().Be(evt.ClosedAtUtc!.Value.AddHours(72));
        (await db.ChatLocks.SingleAsync(l => l.CrisisEventId == eventId)).ReleasedAtUtc.Should().NotBeNull();
        (await db.AuditEntries.CountAsync(a => a.EntityId == eventId.ToString() && (a.Action == "crisis.close_requested" || a.Action == "crisis.close_cosigned"))).Should().Be(2);
    }
}
