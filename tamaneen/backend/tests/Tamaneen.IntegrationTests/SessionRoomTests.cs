using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tamaneen.Hosting;
using Tamaneen.SharedKernel;
using static Tamaneen.IntegrationTests.JourneyHosts;

namespace Tamaneen.IntegrationTests;

/// <summary>
/// The session itself through the real API and crisis worker: the join window (from 10 minutes before), the session
/// pass bound to one appointment and one side, presence both ways, P54 lateness and its free reschedule, the
/// pre-session note, P43 feedback through the gate, and row-level security for each role.
/// </summary>
[Collection(SqlServerGroup.Name)]
public sealed class SessionRoomTests : IAsyncLifetime
{
    private const string Password = "Quiet-Morning-Walk-77";
    private const string Sara = "sara@staff.tamaneen.dev";
    private readonly SqlServerFixture _sql;
    private JourneyHosts _hosts = null!;

    public SessionRoomTests(SqlServerFixture sql) => _sql = sql;

    public Task InitializeAsync()
    {
        _hosts = new JourneyHosts(_sql);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _hosts.DisposeAsync();

    [Fact]
    public async Task Patient_and_therapist_join_only_inside_the_window_and_see_each_other()
    {
        var (me, _) = await BookablePatientAsync("ar");
        using var sara = _hosts.ApiClient("ar", await _hosts.StaffSignInAsync(Sara));
        var saraId = (await Read(await sara.GetAsync("/v1/staff/me"))).GetProperty("userId").GetGuid();
        var appointment = await BookAsync(me, saraId, 0);

        // Days ahead: the room is closed for both sides.
        var closed = await Problem(await me.PostAsync($"/v1/appointments/{appointment}/join", null), 409);
        closed.GetProperty("code").GetString().Should().Be(ErrorCodes.SessionNotOpen);
        closed.TryGetProperty("opensAt", out _).Should().BeTrue();
        (await Problem(await sara.PostAsync($"/v1/staff/appointments/{appointment}/join", null), 409)).GetProperty("code").GetString().Should().Be(ErrorCodes.SessionNotOpen);

        // Eleven minutes before: still closed. Five minutes before: open.
        await MoveStartAsync(appointment, TimeSpan.FromMinutes(11));
        await Problem(await me.PostAsync($"/v1/appointments/{appointment}/join", null), 409);
        await MoveStartAsync(appointment, TimeSpan.FromMinutes(5));

        var lobby = await Read(await me.GetAsync($"/v1/appointments/{appointment}/presence"));
        lobby.GetProperty("open").GetBoolean().Should().BeTrue();
        lobby.GetProperty("peer").GetProperty("present").GetBoolean().Should().BeFalse();
        lobby.GetProperty("therapistLate").GetBoolean().Should().BeFalse();

        var join = await Read(await me.PostAsync($"/v1/appointments/{appointment}/join", null));
        join.GetProperty("provider").GetString().Should().Be("dev");
        join.GetProperty("mode").GetString().Should().Be("test_room");
        join.GetProperty("participant").GetString().Should().Be("patient");
        var patientPass = join.GetProperty("token").GetString()!;
        join.GetProperty("expiresAt").GetDateTimeOffset().Should().BeOnOrBefore(join.GetProperty("closesAt").GetDateTimeOffset());

        // The therapist's page sees the patient in the room.
        var page = await Read(await sara.GetAsync($"/v1/staff/appointments/{appointment}/session"));
        page.GetProperty("state").GetProperty("peer").GetProperty("present").GetBoolean().Should().BeTrue();
        page.GetProperty("state").GetProperty("patientCrisisOpen").GetBoolean().Should().BeFalse();

        var staffJoin = await Read(await sara.PostAsync($"/v1/staff/appointments/{appointment}/join", null));
        staffJoin.GetProperty("participant").GetString().Should().Be("therapist");
        var therapistPass = staffJoin.GetProperty("token").GetString()!;

        var state = await Read(await me.PostAsJsonAsync($"/v1/appointments/{appointment}/presence", new { token = patientPass }));
        state.GetProperty("peer").GetProperty("present").GetBoolean().Should().BeTrue("the therapist has joined");

        // Each pass works only for its own side.
        (await Problem(await me.PostAsJsonAsync($"/v1/appointments/{appointment}/presence", new { token = therapistPass }), 403)).GetProperty("code").GetString().Should().Be(ErrorCodes.SessionTokenInvalid);
        await Problem(await sara.PostAsJsonAsync($"/v1/staff/appointments/{appointment}/presence", new { token = patientPass }), 403);
        await Problem(await me.PostAsJsonAsync($"/v1/appointments/{appointment}/presence", new { token = patientPass[..^2] + "AA" }), 403);
        await Problem(await me.PostAsJsonAsync($"/v1/appointments/{appointment}/presence", new { token = (string?)null }), 403);

        // The therapist leaves: the patient no longer sees them.
        await Read(await sara.PostAsJsonAsync($"/v1/staff/appointments/{appointment}/presence", new { token = therapistPass, leaving = true }));
        (await Read(await me.GetAsync($"/v1/appointments/{appointment}/presence"))).GetProperty("peer").GetProperty("present").GetBoolean().Should().BeFalse();

        await using var audit = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor));
        (await audit.AuditEntries.CountAsync(e => e.Action == "session.joined" && e.EntityId == appointment.ToString())).Should().Be(2);
        (await audit.AuditEntries.CountAsync(e => e.Action == "session.opened" && e.EntityId == appointment.ToString())).Should().Be(1);
    }

    [Fact]
    public async Task A_pass_is_bound_to_its_appointment_and_nobody_else_reaches_the_room()
    {
        var (me, _) = await BookablePatientAsync("en");
        var (stranger, _) = await BookablePatientAsync("en");
        using var sara = _hosts.ApiClient("en", await _hosts.StaffSignInAsync(Sara));
        using var khalid = _hosts.ApiClient("en", await _hosts.StaffSignInAsync("khalid@staff.tamaneen.dev"));
        var saraId = (await Read(await sara.GetAsync("/v1/staff/me"))).GetProperty("userId").GetGuid();
        var first = await BookAsync(me, saraId, 0);
        var second = await BookAsync(me, saraId, 1);
        await MoveStartAsync(first, TimeSpan.FromMinutes(2));

        var pass = (await Read(await me.PostAsync($"/v1/appointments/{first}/join", null))).GetProperty("token").GetString();
        (await Problem(await me.PostAsJsonAsync($"/v1/appointments/{second}/presence", new { token = pass }), 403))
            .GetProperty("code").GetString().Should().Be(ErrorCodes.SessionTokenInvalid, "a pass for one appointment does not open another");

        // Another patient and another therapist: the appointment does not exist for them.
        await Problem(await stranger.PostAsync($"/v1/appointments/{first}/join", null), 404);
        await Problem(await stranger.GetAsync($"/v1/appointments/{first}/presence"), 404);
        await Problem(await stranger.PostAsJsonAsync($"/v1/appointments/{first}/feedback", new { rating = "helpful" }), 404);
        await Problem(await khalid.PostAsync($"/v1/staff/appointments/{first}/join", null), 404);
        await Problem(await khalid.GetAsync($"/v1/staff/appointments/{first}/session"), 404);

        // Row-level security on the presence rows themselves.
        var khalidId = (await Read(await khalid.GetAsync("/v1/staff/me"))).GetProperty("userId").GetGuid();
        await using (var asKhalid = _sql.Context(StaticCurrentUser.ForStaff(khalidId, AppRoles.Therapist)))
        {
            (await asKhalid.SessionPresence.CountAsync(s => s.AppointmentId == first)).Should().Be(0);
        }

        await using (var asSara = _sql.Context(StaticCurrentUser.ForStaff(saraId, AppRoles.Therapist)))
        {
            (await asSara.SessionPresence.CountAsync(s => s.AppointmentId == first)).Should().Be(1);
        }

        await using var asAdmin = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Admin));
        (await asAdmin.SessionPresence.CountAsync(s => s.AppointmentId == first)).Should().Be(0);
    }

    [Fact]
    public async Task A_late_therapist_turns_the_lobby_into_P54_and_the_patient_may_move_the_session_for_free()
    {
        var (me, _) = await BookablePatientAsync("ar");
        using var sara = _hosts.ApiClient("ar", await _hosts.StaffSignInAsync(Sara));
        var saraId = (await Read(await sara.GetAsync("/v1/staff/me"))).GetProperty("userId").GetGuid();
        var appointment = await BookAsync(me, saraId, 0);
        await MoveStartAsync(appointment, TimeSpan.FromMinutes(-7));

        var state = await Read(await me.GetAsync($"/v1/appointments/{appointment}/presence"));
        state.GetProperty("therapistLate").GetBoolean().Should().BeTrue();
        state.GetProperty("lateOptionsAfterMinutes").GetInt32().Should().Be(15);
        (await Read(await me.GetAsync($"/v1/appointments/{appointment}"))).TryGetProperty("therapistLateMinutes", out _).Should().BeTrue();

        // Within 24 hours a move is normally refused; with the therapist late it is free.
        var slots = await Read(await me.GetAsync($"/v1/therapists/{saraId}/slots?days=14"));
        var target = slots.EnumerateArray().Skip(3).First().GetProperty("slotId").GetGuid();
        var moved = await Read(await me.PostAsJsonAsync($"/v1/appointments/{appointment}/reschedule", new { slotId = target }));
        moved.GetProperty("rescheduled").GetBoolean().Should().BeTrue();
        await using var audit = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor));
        (await audit.AuditEntries.AnyAsync(e => e.Action == "appointment.rescheduled" && e.EntityId == appointment.ToString() && e.Reason == "therapist_late")).Should().BeTrue();
    }

    [Fact]
    public async Task The_therapist_joining_late_ends_the_late_state_keeps_the_full_length_and_is_recorded()
    {
        var (me, _) = await BookablePatientAsync("en");
        using var sara = _hosts.ApiClient("en", await _hosts.StaffSignInAsync(Sara));
        var saraId = (await Read(await sara.GetAsync("/v1/staff/me"))).GetProperty("userId").GetGuid();
        var appointment = await BookAsync(me, saraId, 0);
        await MoveStartAsync(appointment, TimeSpan.FromMinutes(-10));

        var join = await Read(await sara.PostAsync($"/v1/staff/appointments/{appointment}/join", null));
        var closes = join.GetProperty("closesAt").GetDateTimeOffset();
        closes.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(45), "the late minutes are not taken from the session");
        var state = await Read(await me.GetAsync($"/v1/appointments/{appointment}/presence"));
        state.GetProperty("therapistLate").GetBoolean().Should().BeFalse();
        state.GetProperty("peer").GetProperty("present").GetBoolean().Should().BeTrue();

        // No longer late: the 24-hour rule applies to moves again.
        var slots = await Read(await me.GetAsync($"/v1/therapists/{saraId}/slots?days=14"));
        var target = slots.EnumerateArray().Skip(3).First().GetProperty("slotId").GetGuid();
        await Problem(await me.PostAsJsonAsync($"/v1/appointments/{appointment}/reschedule", new { slotId = target }), 409);

        await using var audit = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor));
        (await audit.AuditEntries.AnyAsync(e => e.Action == "session.therapist_late" && e.EntityId == appointment.ToString())).Should().BeTrue();
    }

    [Fact]
    public async Task The_pre_session_note_reaches_the_therapist_encrypted_and_audited()
    {
        var (me, _) = await BookablePatientAsync("ar");
        using var sara = _hosts.ApiClient("ar", await _hosts.StaffSignInAsync(Sara));
        var saraId = (await Read(await sara.GetAsync("/v1/staff/me"))).GetProperty("userId").GetGuid();
        var appointment = await BookAsync(me, saraId, 0);
        const string text = "النوم تحسن، لكن ما زلت قلقًا من اجتماع الأحد.";

        var saved = await Read(await me.PutAsJsonAsync($"/v1/appointments/{appointment}/pre-session-note", new { text }));
        saved.GetProperty("crisis").GetBoolean().Should().BeFalse();
        (await Read(await me.GetAsync($"/v1/appointments/{appointment}/pre-session-note"))).GetProperty("text").GetString().Should().Be(text);
        await Problem(await me.PutAsJsonAsync($"/v1/appointments/{appointment}/pre-session-note", new { text = new string('a', 1001) }), 400);

        var page = await Read(await sara.GetAsync($"/v1/staff/appointments/{appointment}/session"));
        page.GetProperty("preSessionNote").GetString().Should().Be(text);

        await using var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor));
        var row = await db.SessionPrep.SingleAsync(s => s.AppointmentId == appointment);
        Encoding.UTF8.GetString(row.NoteCipher).Should().NotContain("اجتماع");
        (await db.AuditEntries.AnyAsync(e => e.Action == "session.opened" && e.EntityId == appointment.ToString() && e.Reason == "pre_session_note")).Should().BeTrue();

        // Cleared: the therapist sees nothing.
        await Read(await me.PutAsJsonAsync($"/v1/appointments/{appointment}/pre-session-note", new { text = "" }));
        (await Read(await sara.GetAsync($"/v1/staff/appointments/{appointment}/session"))).GetProperty("preSessionNote").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Feedback_is_given_once_after_the_start_and_stays_out_of_the_therapists_reach()
    {
        var (me, _) = await BookablePatientAsync("en");
        using var sara = _hosts.ApiClient("en", await _hosts.StaffSignInAsync(Sara));
        var saraId = (await Read(await sara.GetAsync("/v1/staff/me"))).GetProperty("userId").GetGuid();
        var appointment = await BookAsync(me, saraId, 0);

        (await Problem(await me.PostAsJsonAsync($"/v1/appointments/{appointment}/feedback", new { rating = "helpful" }), 409))
            .GetProperty("code").GetString().Should().Be(ErrorCodes.SessionFeedbackNotYet);
        await MoveStartAsync(appointment, TimeSpan.FromMinutes(-55));
        ((int)(await me.GetAsync($"/v1/appointments/{appointment}/feedback")).StatusCode).Should().Be(404);
        (await Problem(await me.PostAsJsonAsync($"/v1/appointments/{appointment}/feedback", new { rating = "brilliant" }), 400)).GetProperty("errors").TryGetProperty("rating", out _).Should().BeTrue();
        await Problem(await me.PostAsJsonAsync($"/v1/appointments/{appointment}/feedback", new { }), 400);

        var saved = await Read(await me.PostAsJsonAsync($"/v1/appointments/{appointment}/feedback", new { rating = "helpful", comfortable = true, comment = "Talking about sleep helped." }));
        saved.GetProperty("crisis").GetBoolean().Should().BeFalse();
        saved.GetProperty("feedback").GetProperty("rating").GetString().Should().Be("helpful");
        (await Read(await me.GetAsync($"/v1/appointments/{appointment}/feedback"))).GetProperty("comment").GetString().Should().Be("Talking about sleep helped.");
        (await Problem(await me.PostAsJsonAsync($"/v1/appointments/{appointment}/feedback", new { rating = "okay" }), 409))
            .GetProperty("code").GetString().Should().Be(ErrorCodes.SessionFeedbackExists);

        await using (var asSara = _sql.Context(StaticCurrentUser.ForStaff(saraId, AppRoles.Therapist)))
        {
            (await asSara.SessionFeedback.CountAsync(f => f.AppointmentId == appointment)).Should().Be(0, "feedback reaches supervisors only");
        }

        await using var asSupervisor = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor));
        var row = await asSupervisor.SessionFeedback.SingleAsync(f => f.AppointmentId == appointment);
        row.Rating.Should().Be(3);
        Encoding.UTF8.GetString(row.CommentCipher!).Should().NotContain("sleep");
    }

    [Fact]
    public async Task A_feedback_comment_passes_the_crisis_gate()
    {
        var (me, _) = await BookablePatientAsync("ar");
        using var sara = _hosts.ApiClient("ar", await _hosts.StaffSignInAsync(Sara));
        var saraId = (await Read(await sara.GetAsync("/v1/staff/me"))).GetProperty("userId").GetGuid();
        var appointment = await BookAsync(me, saraId, 0);
        await MoveStartAsync(appointment, TimeSpan.FromMinutes(-5));
        await Read(await sara.PostAsync($"/v1/staff/appointments/{appointment}/join", null));

        var saved = await Read(await me.PostAsJsonAsync($"/v1/appointments/{appointment}/feedback", new { rating = "not_helpful", comment = "ما عاد أتحمل، أبي أموت الليلة" }));
        saved.GetProperty("crisis").GetBoolean().Should().BeTrue();
        saved.GetProperty("script").GetString().Should().NotBeNullOrEmpty();

        // The therapist's session page now says the patient has an open crisis event.
        (await Read(await sara.GetAsync($"/v1/staff/appointments/{appointment}/presence"))).GetProperty("patientCrisisOpen").GetBoolean().Should().BeTrue();
    }

    /// <summary>Moves a booked appointment so that it starts <paramref name="fromNow"/> from now (as a supervisor would see it).</summary>
    private async Task MoveStartAsync(Guid appointmentId, TimeSpan fromNow)
    {
        await using var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor));
        var start = DateTimeOffset.UtcNow + fromNow;
        (await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE clinical.Appointments SET StartsAtUtc = {start} WHERE Id = {appointmentId}")).Should().Be(1);
    }

    private static async Task<Guid> BookAsync(HttpClient me, Guid therapistId, int slotIndex)
    {
        var slots = await Read(await me.GetAsync($"/v1/therapists/{therapistId}/slots?days=14"));
        var slot = slots.EnumerateArray().Skip(slotIndex).First().GetProperty("slotId").GetGuid();
        var hold = await Read(await me.PostAsJsonAsync("/v1/bookings/holds", new { slotId = slot }));
        var confirmed = await Read(await me.PostAsync($"/v1/bookings/holds/{hold.GetProperty("holdId").GetGuid()}/confirm", null));
        return confirmed.GetProperty("id").GetGuid();
    }

    /// <summary>Consents, safety info, the direct path and a negative safety check: the patient may book.</summary>
    private async Task<(HttpClient Client, string Email)> BookablePatientAsync(string language)
    {
        var email = $"sr-{Guid.NewGuid():N}@example.com";
        using var anon = _hosts.ApiClient(language);
        var eligibility = await Read(await anon.PostAsJsonAsync("/v1/auth/eligibility", new { isAdult = true, cityCode = "RUH" }));
        var challenge = await Read(await anon.PostAsJsonAsync("/v1/auth/signup", new { eligibilityToken = eligibility.GetProperty("eligibilityToken").GetString(), nickname = "Calm Palm", contactType = "email", contact = email, password = Password, language }));
        var verified = await Read(await anon.PostAsJsonAsync("/v1/auth/signup/verify", new { challengeId = challenge.GetProperty("challengeId").GetGuid(), code = _hosts.CodeSentTo(email) }));
        var me = _hosts.ApiClient(language, verified.GetProperty("tokens").GetProperty("accessToken").GetString());
        await Read(await me.PutAsJsonAsync("/v1/me/consents", new { items = new[] { "platform", "ai", "share_with_therapist" }.Select(k => new { key = k, granted = true }) }));
        await Read(await PutSafetyInfoAsync(me, new { shareLocation = false }));
        await Read(await me.PutAsJsonAsync("/v1/me/path", new { path = "direct" }));
        await Read(await me.PostAsJsonAsync("/v1/screening/cssrs", new { answers = new[] { false, false, false, false, false, false } }));
        return (me, email);
    }
}
