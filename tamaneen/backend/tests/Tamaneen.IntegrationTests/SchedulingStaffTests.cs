using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tamaneen.Api;
using Tamaneen.Api.Auth;
using Tamaneen.Api.Dev;
using Tamaneen.Api.Endpoints;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Hosting;
using Tamaneen.Identity;
using Tamaneen.Identity.Auth;
using Tamaneen.Infrastructure.Auth;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;
using Tamaneen.SharedKernel.Notifications;
using static Tamaneen.IntegrationTests.JourneyHosts;

namespace Tamaneen.IntegrationTests;

/// <summary>
/// S19 availability and S18 schedule through the real API, with a fresh therapist per test so the seeded
/// directory the other tests rely on is never changed. Notifications are recorded instead of sent.
/// </summary>
[Collection(SqlServerGroup.Name)]
public sealed class SchedulingStaffTests : IAsyncLifetime
{
    private const string Password = "Quiet-Morning-Walk-77";
    private static readonly TimeZoneInfo Riyadh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
    private readonly SqlServerFixture _sql;
    private readonly List<Guid> _therapists = [];
    private JourneyHosts _hosts = null!;
    private WebApplicationFactory<SharedResource> _api = null!;

    public SchedulingStaffTests(SqlServerFixture sql) => _sql = sql;

    private RecordingNotifier Notices { get; } = new();

    public Task InitializeAsync()
    {
        _hosts = new JourneyHosts(_sql);
        _api = _hosts.Api.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<INotifier>(Notices)));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        // Take the test therapists out of the directory and matching for the tests that run after these.
        if (_therapists.Count > 0)
        {
            await using var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.SchedulingService));
            foreach (var id in _therapists)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [clinical].[TherapistProfiles] SET [AcceptingNewPatients] = 0 WHERE [UserId] = {id}");
            }
        }

        await _api.DisposeAsync();
        await _hosts.DisposeAsync();
    }

    [Fact]
    public async Task The_weekly_pattern_generates_times_without_overlaps_and_never_touches_booked_ones()
    {
        var (therapistId, me) = await NewTherapistAsync();
        var empty = await Read(await me.GetAsync("/v1/staff/availability"));
        empty.GetProperty("slots").GetArrayLength().Should().Be(0);
        empty.GetProperty("sessionMinutes").GetInt32().Should().Be(50);

        var overlap = await Problem(await me.PutAsJsonAsync("/v1/staff/availability", new
        {
            sessionMinutes = 50, bufferMinutes = 10,
            weekly = new[] { new { day = 0, start = "10:00", end = "13:00" }, new { day = 0, start = "12:00", end = "14:00" } },
        }), 400);
        overlap.GetProperty("errors").TryGetProperty("weekly.1", out _).Should().BeTrue();

        var everyDay = Enumerable.Range(0, 7).Select(d => new { day = d, start = "10:00", end = "12:00" }).ToArray();
        var saved = await Read(await me.PutAsJsonAsync("/v1/staff/availability", new { sessionMinutes = 50, bufferMinutes = 10, weekly = everyDay }));
        saved.GetProperty("added").GetInt32().Should().BeInRange(108, 112, "10:00 and 11:00 on each of 56 days, less today's past ones");
        var slots = Slots(saved.GetProperty("availability"));
        slots.Should().OnlyContain(s => s.Minutes == 50);
        slots.Zip(slots.Skip(1)).Should().OnlyContain(p => p.First.End <= p.Second.Start, "times never overlap");
        slots.Select(s => TimeZoneInfo.ConvertTime(s.Start, Riyadh).Hour).Distinct().Should().BeEquivalentTo([10, 11]);

        // Saving the same pattern again changes nothing.
        var again = await Read(await me.PutAsJsonAsync("/v1/staff/availability", new { sessionMinutes = 50, bufferMinutes = 10, weekly = everyDay }));
        again.GetProperty("added").GetInt32().Should().Be(0);
        again.GetProperty("removed").GetInt32().Should().Be(0);

        // A patient books one time.
        var (patient, _) = await BookablePatientAsync();
        var booked = await BookAsync(patient, therapistId);

        // Leave covering everything and a shorter week: the booked time stays, locked, and is reported.
        var cleared = await Read(await me.PutAsJsonAsync("/v1/staff/availability", new { sessionMinutes = 50, bufferMinutes = 10, weekly = Array.Empty<object>() }));
        cleared.GetProperty("removed").GetInt32().Should().BeGreaterThan(100);
        cleared.GetProperty("affected").EnumerateArray().Select(a => a.GetProperty("slotId").GetGuid()).Should().Equal(booked.SlotId);
        var left = cleared.GetProperty("availability").GetProperty("slots").EnumerateArray().ToList();
        left.Should().ContainSingle(s => s.GetProperty("slotId").GetGuid() == booked.SlotId && s.GetProperty("status").GetString() == "booked");
        left.Where(s => s.GetProperty("status").GetString() == "open").Should().BeEmpty();
        (await Read(await me.GetAsync("/v1/staff/availability"))).GetProperty("affected").GetArrayLength().Should().Be(1, "the warning stays until the booked time passes");

        (await Problem(await me.DeleteAsync($"/v1/staff/availability/slots/{booked.SlotId}"), 409)).GetProperty("code").GetString().Should().Be(ErrorCodes.SlotTaken);
        (await Problem(await me.PatchAsJsonAsync($"/v1/staff/availability/slots/{booked.SlotId}", new { startsAt = booked.StartsAt.AddDays(1) }), 409)).GetProperty("code").GetString().Should().Be(ErrorCodes.SlotTaken);

        // One-off times: never overlapping, never within the 2-hour notice.
        (await Problem(await me.PostAsJsonAsync("/v1/staff/availability/slots", new { startsAt = booked.StartsAt.AddMinutes(20) }), 409)).GetProperty("code").GetString().Should().Be(ErrorCodes.SlotOverlap);
        (await Problem(await me.PostAsJsonAsync("/v1/staff/availability/slots", new { startsAt = DateTimeOffset.UtcNow.AddHours(1) }), 400)).GetProperty("code").GetString().Should().Be(ErrorCodes.SlotTooSoon);
        var extra = await Read(await me.PostAsJsonAsync("/v1/staff/availability/slots", new { startsAt = booked.StartsAt.AddHours(3) }));
        extra.GetProperty("source").GetString().Should().Be("manual");
        var extraId = extra.GetProperty("slotId").GetGuid();
        var moved = await Read(await me.PatchAsJsonAsync($"/v1/staff/availability/slots/{extraId}", new { startsAt = booked.StartsAt.AddHours(4) }));
        moved.GetProperty("startsAt").GetDateTimeOffset().Should().Be(booked.StartsAt.AddHours(4));
        (await me.DeleteAsync($"/v1/staff/availability/slots/{extraId}")).IsSuccessStatusCode.Should().BeTrue();
        Slots(await Read(await me.GetAsync("/v1/staff/availability"))).Should().NotContain(s => s.Id == extraId);
    }

    [Fact]
    public async Task Only_the_therapist_changes_their_times_and_a_supervisor_may_read_them()
    {
        var (therapistId, me) = await NewTherapistAsync();
        var slot = await Read(await me.PostAsJsonAsync("/v1/staff/availability/slots", new { startsAt = WeekdayAt(2, 14) }));
        var slotId = slot.GetProperty("slotId").GetGuid();

        using var noura = Client(await _hosts.StaffSignInAsync("noura@staff.tamaneen.dev"));
        await Problem(await noura.GetAsync($"/v1/staff/availability?therapistId={therapistId}"), 404);
        await Problem(await noura.DeleteAsync($"/v1/staff/availability/slots/{slotId}"), 404);
        await Problem(await noura.PatchAsJsonAsync($"/v1/staff/availability/slots/{slotId}", new { startsAt = WeekdayAt(3, 14) }), 404);

        using var fahad = Client(await _hosts.StaffSignInAsync("fahad@staff.tamaneen.dev"));
        Slots(await Read(await fahad.GetAsync($"/v1/staff/availability?therapistId={therapistId}"))).Should().ContainSingle(s => s.Id == slotId);
        await Problem(await fahad.PutAsJsonAsync("/v1/staff/availability", new { weekly = Array.Empty<object>() }), 403);
        await Problem(await fahad.PostAsJsonAsync("/v1/staff/availability/slots", new { startsAt = WeekdayAt(3, 15) }), 403);

        var (patient, _) = await BookablePatientAsync();
        await Problem(await patient.GetAsync("/v1/staff/availability"), 403);
        await Problem(await patient.GetAsync("/v1/staff/schedule"), 403);
    }

    [Fact]
    public async Task A_new_time_runs_the_waitlist_check()
    {
        var (therapistId, me) = await NewTherapistAsync();
        var (patient, _) = await BookablePatientAsync();
        (await Read(await patient.PutAsJsonAsync("/v1/me/waitlist", new { therapistId })))
            .GetProperty("status").GetString().Should().Be("waiting", "the new therapist has no times yet");

        var slot = await Read(await me.PostAsJsonAsync("/v1/staff/availability/slots", new { startsAt = WeekdayAt(1, 14) }));
        var waitlist = await Read(await patient.GetAsync("/v1/me/waitlist"));
        waitlist.GetProperty("status").GetString().Should().Be("available");
        waitlist.GetProperty("offer").GetProperty("therapistId").GetGuid().Should().Be(therapistId);
        slot.GetProperty("slotId").GetGuid().Should().NotBeEmpty();
        await Read(await patient.DeleteAsync("/v1/me/waitlist"));
    }

    [Fact]
    public async Task The_therapist_runs_late_cancels_and_records_a_missed_session_and_the_patient_sees_each()
    {
        var (therapistId, me) = await NewTherapistAsync();
        var (patient, _) = await BookablePatientAsync();
        var (stranger, _) = await BookablePatientAsync();
        await Read(await me.PostAsJsonAsync("/v1/staff/availability/slots", new { startsAt = WeekdayAt(2, 14) }));
        await Read(await me.PostAsJsonAsync("/v1/staff/availability/slots", new { startsAt = WeekdayAt(3, 14) }));
        var first = await BookAsync(patient, therapistId);
        var second = await BookAsync(patient, therapistId);
        var pseudonym = (await Read(await patient.GetAsync("/v1/me"))).GetProperty("pseudonymId").GetString();
        Guid patientId;
        await using (var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor)))
        {
            patientId = await db.UserAccounts.Where(a => a.PseudonymId == pseudonym).Select(a => a.Id).SingleAsync();
        }

        var detail = await Read(await patient.GetAsync($"/v1/appointments/{first.AppointmentId}"));
        detail.GetProperty("therapistLateMinutes").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("cancelledBy").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("cancelReasonCode").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("missed").GetBoolean().Should().BeFalse();

        // Running late only around the session.
        (await Problem(await me.PostAsJsonAsync($"/v1/staff/appointments/{first.AppointmentId}/running-late", new { minutes = 10 }), 409))
            .GetProperty("code").GetString().Should().Be(ErrorCodes.AppointmentNotLateable);
        await MoveAppointmentAsync(first.AppointmentId, DateTimeOffset.UtcNow.AddMinutes(5));
        await Problem(await me.PostAsJsonAsync($"/v1/staff/appointments/{first.AppointmentId}/running-late", new { minutes = 0 }), 400);
        var late = await Read(await me.PostAsJsonAsync($"/v1/staff/appointments/{first.AppointmentId}/running-late", new { minutes = 10 }));
        late.GetProperty("therapistLateMinutes").GetInt32().Should().Be(10);
        (await Read(await patient.GetAsync($"/v1/appointments/{first.AppointmentId}"))).GetProperty("therapistLateMinutes").GetInt32().Should().Be(10);
        (await Read(await patient.GetAsync("/v1/appointments"))).GetProperty("upcoming").EnumerateArray()
            .Single(a => a.GetProperty("id").GetGuid() == first.AppointmentId).GetProperty("therapistLateMinutes").GetInt32().Should().Be(10);
        Notices.For(patientId).Should().ContainSingle(n => n.Kind == "appointment.running_late" && n.Data["minutes"] == "10");

        // Another therapist and another patient see nothing of it.
        using var noura = Client(await _hosts.StaffSignInAsync("noura@staff.tamaneen.dev"));
        await Problem(await noura.PostAsJsonAsync($"/v1/staff/appointments/{first.AppointmentId}/cancel", new { reasonCode = "emergency" }), 404);
        await Problem(await noura.GetAsync($"/v1/staff/appointments/{first.AppointmentId}"), 404);
        await Problem(await stranger.GetAsync($"/v1/appointments/{first.AppointmentId}"), 404);

        // Cancel needs a known reason; the patient never sees which; the time is taken out of the calendar.
        (await Problem(await me.PostAsJsonAsync($"/v1/staff/appointments/{first.AppointmentId}/cancel", new { reasonCode = "bored" }), 400))
            .GetProperty("errors").TryGetProperty("reasonCode", out _).Should().BeTrue();
        var cancelled = await Read(await me.PostAsJsonAsync($"/v1/staff/appointments/{first.AppointmentId}/cancel", new { reasonCode = "illness", note = "Flu, back next week." }));
        cancelled.GetProperty("cancelledBy").GetString().Should().Be("therapist");
        cancelled.GetProperty("cancelReasonCode").GetString().Should().Be("illness");
        var seen = await Read(await patient.GetAsync($"/v1/appointments/{first.AppointmentId}"));
        seen.GetProperty("status").GetString().Should().Be("cancelled_by_therapist");
        seen.GetProperty("cancelledBy").GetString().Should().Be("therapist");
        seen.GetProperty("cancelReasonCode").ValueKind.Should().Be(JsonValueKind.Null, "the therapist's real reason is not shown to the patient");
        Notices.For(patientId).Should().Contain(n => n.Kind == "appointment.cancelled_by_therapist" && n.Link == $"/appointments/{first.AppointmentId}/cancelled");
        Notices.All.Should().Contain(n => n.Kind == "appointment.rebook_needed" && n.RecipientUserId != patientId);
        Notices.All.SelectMany(n => n.Data.Values).Should().NotContain(v => v.Contains("Flu", StringComparison.Ordinal));
        Slots(await Read(await me.GetAsync("/v1/staff/availability"))).Should().Contain(s => s.Id == first.SlotId && s.Status == "blocked", "the therapist said they cannot make that time");
        (await Read(await patient.GetAsync("/v1/home"))).GetProperty("appointmentNotice").GetProperty("kind").GetString().Should().Be("cancelled");
        await Problem(await me.PostAsJsonAsync($"/v1/staff/appointments/{first.AppointmentId}/cancel", new { reasonCode = "illness" }), 409);

        // The second session is missed.
        await MoveAppointmentAsync(second.AppointmentId, DateTimeOffset.UtcNow.AddMinutes(-60));
        var schedule = await Read(await me.GetAsync($"/v1/staff/schedule?from={TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow.AddMinutes(-60), Riyadh):yyyy-MM-dd}&days=1"));
        var row = schedule.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == second.AppointmentId);
        row.GetProperty("canRecordOutcome").GetBoolean().Should().BeTrue();
        row.GetProperty("pseudonymId").GetString().Should().StartWith("P-");
        await Read(await me.PostAsJsonAsync($"/v1/staff/appointments/{second.AppointmentId}/outcome", new { outcome = "no_show" }));
        var missed = await Read(await patient.GetAsync($"/v1/appointments/{second.AppointmentId}"));
        missed.GetProperty("missed").GetBoolean().Should().BeTrue();
        missed.GetProperty("status").GetString().Should().Be("no_show");
        Notices.For(patientId).Should().Contain(n => n.Kind == "appointment.missed" && n.Link == $"/appointments/{second.AppointmentId}/missed");
        (await Read(await patient.GetAsync("/v1/home"))).GetProperty("appointmentNotice").GetProperty("kind").GetString().Should().Be("missed");
    }

    [Fact]
    public async Task The_therapist_reschedules_to_one_of_their_own_free_times_and_the_patient_is_told()
    {
        var (therapistId, me) = await NewTherapistAsync();
        var (_, other) = await NewTherapistAsync();
        var (patient, _) = await BookablePatientAsync();
        await Read(await me.PostAsJsonAsync("/v1/staff/availability/slots", new { startsAt = WeekdayAt(2, 14) }));
        await Read(await me.PostAsJsonAsync("/v1/staff/availability/slots", new { startsAt = WeekdayAt(3, 14) }));
        var otherSlot = await Read(await other.PostAsJsonAsync("/v1/staff/availability/slots", new { startsAt = WeekdayAt(3, 15) }));
        var booking = await BookAsync(patient, therapistId);

        // Only the therapist's own free times are offered.
        var free = (await Read(await me.GetAsync("/v1/staff/free-slots"))).EnumerateArray().Select(s => s.GetProperty("slotId").GetGuid()).ToList();
        free.Should().NotContain(booking.SlotId).And.NotContain(otherSlot.GetProperty("slotId").GetGuid()).And.HaveCount(1);
        var target = free.Single();

        // A reason is required, another therapist's time is refused, another therapist cannot move it.
        (await Problem(await me.PostAsJsonAsync($"/v1/staff/appointments/{booking.AppointmentId}/reschedule", new { slotId = target }), 400))
            .GetProperty("errors").TryGetProperty("reasonCode", out _).Should().BeTrue();
        await Problem(await me.PostAsJsonAsync($"/v1/staff/appointments/{booking.AppointmentId}/reschedule", new { slotId = otherSlot.GetProperty("slotId").GetGuid(), reasonCode = "illness" }), 409);
        await Problem(await other.PostAsJsonAsync($"/v1/staff/appointments/{booking.AppointmentId}/reschedule", new { slotId = target, reasonCode = "illness" }), 404);

        var moved = await Read(await me.PostAsJsonAsync($"/v1/staff/appointments/{booking.AppointmentId}/reschedule", new { slotId = target, reasonCode = "schedule_conflict" }));
        moved.GetProperty("startsAt").GetDateTimeOffset().Should().Be(WeekdayAt(3, 14));
        var seen = await Read(await patient.GetAsync($"/v1/appointments/{booking.AppointmentId}"));
        seen.GetProperty("rescheduled").GetBoolean().Should().BeTrue();
        seen.GetProperty("startsAt").GetDateTimeOffset().Should().Be(WeekdayAt(3, 14));
        Notices.All.Should().Contain(n => n.Kind == "appointment.rescheduled" && n.Link == $"/appointments/{booking.AppointmentId}" && n.Data.ContainsKey("therapistName"));
        Slots(await Read(await me.GetAsync("/v1/staff/availability"))).Should().Contain(s => s.Id == booking.SlotId && s.Status == "blocked");
        (await Read(await me.GetAsync("/v1/staff/free-slots"))).GetArrayLength().Should().Be(0);

        await using var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor));
        (await db.AuditEntries.AnyAsync(e => e.Action == "appointment.rescheduled" && e.EntityId == booking.AppointmentId.ToString() && e.ActorUserId == therapistId && e.Reason!.StartsWith("therapist:schedule_conflict")))
            .Should().BeTrue();
    }

    // ---------- helpers ----------

    private sealed record SlotRow(Guid Id, DateTimeOffset Start, DateTimeOffset End, int Minutes, string Status);

    private sealed record Booking(Guid AppointmentId, Guid SlotId, DateTimeOffset StartsAt);

    private static List<SlotRow> Slots(JsonElement availability) => availability.GetProperty("slots").EnumerateArray().Select(s =>
    {
        var start = s.GetProperty("startsAt").GetDateTimeOffset();
        var end = s.GetProperty("endsAt").GetDateTimeOffset();
        return new SlotRow(s.GetProperty("slotId").GetGuid(), start, end, (int)(end - start).TotalMinutes, s.GetProperty("status").GetString()!);
    }).ToList();

    /// <summary>The <paramref name="weeksAhead"/>-th Sunday-to-Thursday day from tomorrow, at the given Riyadh hour.</summary>
    private static DateTimeOffset WeekdayAt(int weeksAhead, int hour)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Riyadh).DateTime);
        var day = Enumerable.Range(1, 60).Select(today.AddDays).Where(d => d.DayOfWeek is not (DayOfWeek.Friday or DayOfWeek.Saturday)).Skip(weeksAhead).First();
        var local = day.ToDateTime(new TimeOnly(hour, 0));
        return new DateTimeOffset(local, Riyadh.GetUtcOffset(local)).ToUniversalTime();
    }

    private HttpClient Client(string? token, string language = "en")
    {
        var client = _api.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    /// <summary>A therapist like the seeded ones (same password and authenticator), with no times yet.</summary>
    private async Task<(Guid Id, HttpClient Client)> NewTherapistAsync()
    {
        var email = $"t-{Guid.NewGuid():N}@staff.tamaneen.dev";
        Guid id;
        await using (var scope = _hosts.Api.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<RequestCurrentUser>().ActAsService(DevSeeder.SchedulerId, AppRoles.SchedulingService);
            var db = sp.GetRequiredService<TamaneenDbContext>();
            var clock = sp.GetRequiredService<IClock>();
            var account = UserAccount.Register(AppRoles.Therapist, sp.GetRequiredService<IContactHasher>().Hash(email), "en", clock);
            account.Activate();
            db.UserAccounts.Add(account);
            var credential = Credential.Create(account.Id, sp.GetRequiredService<PasswordService>().Hash(DevSeeder.StaffPassword), clock);
            credential.BeginTotpSetup(sp.GetRequiredService<ServerSecrets>().Protect(StepUpVerifier.TotpPurpose, Convert.ToBase64String(DevSeeder.StaffTotpSecret)));
            credential.EnableTotp(Totp.StepAt(clock.UtcNow) - 10);
            db.Credentials.Add(credential);
            db.StaffProfiles.Add(StaffProfile.Create(account.Id, "أ. تجربة", "Test T.", null));
            db.TherapistProfiles.Add(TherapistProfile.Create(account.Id, "أخصائي", "Therapist", "نبذة", "Bio", "male", ["trauma"], ["en"], licenseVerified: true));
            await db.SaveChangesAsync();
            id = account.Id;
        }

        _therapists.Add(id);
        return (id, Client(await _hosts.StaffSignInAsync(email)));
    }

    private async Task MoveAppointmentAsync(Guid appointmentId, DateTimeOffset startsAt)
    {
        await using var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [clinical].[Appointments] SET [StartsAtUtc] = {startsAt} WHERE [Id] = {appointmentId}");
    }

    private static async Task<Booking> BookAsync(HttpClient patient, Guid therapistId)
    {
        var slots = await Read(await patient.GetAsync($"/v1/therapists/{therapistId}/slots?days=30"));
        var slot = slots.EnumerateArray().First();
        var hold = await Read(await patient.PostAsJsonAsync("/v1/bookings/holds", new { slotId = slot.GetProperty("slotId").GetGuid() }));
        var appointment = await Read(await patient.PostAsync($"/v1/bookings/holds/{hold.GetProperty("holdId").GetGuid()}/confirm", null));
        return new Booking(appointment.GetProperty("id").GetGuid(), slot.GetProperty("slotId").GetGuid(), slot.GetProperty("startsAt").GetDateTimeOffset());
    }

    /// <summary>Consents, safety info, the direct path and a negative safety check: the patient may book.</summary>
    private async Task<(HttpClient Client, string Email)> BookablePatientAsync(string language = "en")
    {
        var email = $"sch-{Guid.NewGuid():N}@example.com";
        using var anon = Client(null, language);
        var eligibility = await Read(await anon.PostAsJsonAsync("/v1/auth/eligibility", new { isAdult = true, cityCode = "RUH" }));
        var challenge = await Read(await anon.PostAsJsonAsync("/v1/auth/signup", new { eligibilityToken = eligibility.GetProperty("eligibilityToken").GetString(), nickname = "Calm Palm", contactType = "email", contact = email, password = Password, language }));
        var code = System.Text.RegularExpressions.Regex.Match(((InMemoryMessageSender)_api.Services.GetRequiredService<IMessageSender>()).LastTo(email)!.Text, @"\d{6}").Value;
        var verified = await Read(await anon.PostAsJsonAsync("/v1/auth/signup/verify", new { challengeId = challenge.GetProperty("challengeId").GetGuid(), code }));
        var me = Client(verified.GetProperty("tokens").GetProperty("accessToken").GetString(), language);
        await Read(await me.PutAsJsonAsync("/v1/me/consents", new { items = new[] { "platform", "ai", "share_with_therapist" }.Select(k => new { key = k, granted = true }) }));
        await Read(await PutSafetyInfoAsync(me, new { shareLocation = false }));
        await Read(await me.PutAsJsonAsync("/v1/me/path", new { path = "direct" }));
        await Read(await me.PostAsJsonAsync("/v1/screening/cssrs", new { answers = new[] { false, false, false, false, false, false } }));
        return (me, email);
    }

    private sealed class RecordingNotifier : INotifier
    {
        private readonly ConcurrentQueue<Notification> _sent = new();

        public List<Notification> All => [.. _sent];

        public List<Notification> For(Guid userId) => All.Where(n => n.RecipientUserId == userId).ToList();

        public Task NotifyAsync(Notification notification, CancellationToken ct)
        {
            _sent.Enqueue(notification);
            return Task.CompletedTask;
        }
    }
}
