using System.Globalization;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tamaneen.Api.Endpoints;
using Tamaneen.Api.Notifications;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Crisis;
using Tamaneen.Hosting;
using Tamaneen.Identity.Notifications;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;
using Tamaneen.SharedKernel.Notifications;
using static Tamaneen.IntegrationTests.JourneyHosts;

namespace Tamaneen.IntegrationTests;

/// <summary>
/// The notification service through the real API: the inbox is the recipient's alone (RLS), quiet hours hold the
/// SMS or e-mail copy except for crisis kinds, reminders happen once across runs and restarts, copies are neutral,
/// bookings reach both sides, and P38 follows a closed crisis event.
/// </summary>
[Collection(SqlServerGroup.Name)]
public sealed class NotificationTests : IAsyncLifetime
{
    private const string Password = "Quiet-Morning-Walk-77";
    private static readonly string[] ClinicalWords =
    [
        "جلسة", "جلستك", "أزمة", "طوارئ", "أخصائي", "معالج", "علاج", "نفسي", "واجب", "خطة الأمان", "اكتئاب", "طمأنينة",
        "session", "crisis", "therap", "mental", "homework", "safety plan", "depress", "Tamaneen",
    ];

    private readonly SqlServerFixture _sql;
    private JourneyHosts _hosts = null!;

    public NotificationTests(SqlServerFixture sql) => _sql = sql;

    public Task InitializeAsync()
    {
        _hosts = new JourneyHosts(_sql);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _hosts.DisposeAsync();

    private Notifier Notifier => _hosts.Api.Services.GetRequiredService<Notifier>();

    [Fact]
    public async Task The_inbox_belongs_to_its_recipient_and_can_be_marked_read()
    {
        var (alice, aliceId, _) = await PatientAsync("ar");
        var (bob, bobId, _) = await PatientAsync("en");
        await SetQuietHoursAsync(alice, null);
        (await Notifier.NotifyAsync(new Notification(aliceId, NotificationKinds.TriageMatchesReady, new Dictionary<string, string>()), null, CancellationToken.None)).Should().BeTrue();

        var inbox = await Read(await alice.GetAsync("/v1/me/inbox"));
        inbox.GetProperty("unreadCount").GetInt32().Should().Be(1);
        var item = inbox.GetProperty("items")[0];
        item.GetProperty("kind").GetString().Should().Be(NotificationKinds.TriageMatchesReady);
        item.GetProperty("text").GetString().Should().Be("اقتراحات الأخصائيين جاهزة لك.");
        item.GetProperty("link").GetString().Should().Be("/matches");
        var id = item.GetProperty("id").GetString();

        // Bob sees nothing of it and cannot mark it read: RLS hides the row.
        (await Read(await bob.GetAsync("/v1/me/inbox"))).GetProperty("items").GetArrayLength().Should().Be(0);
        await Problem(await bob.PostAsync($"/v1/me/inbox/{id}/read", null), 404);
        await using (var db = _sql.Context(StaticCurrentUser.ForPatient(bobId)))
        {
            (await db.Notifications.CountAsync(n => n.RecipientUserId == aliceId)).Should().Be(0);
        }

        await using (var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor)))
        {
            (await db.Notifications.CountAsync(n => n.RecipientUserId == aliceId)).Should().Be(0, "staff do not read patients' inboxes either");
        }

        await Read(await alice.PostAsync($"/v1/me/inbox/{id}/read", null));
        (await Read(await alice.GetAsync("/v1/me/inbox/unread-count"))).GetProperty("unreadCount").GetInt32().Should().Be(0);
        (await Read(await alice.GetAsync("/v1/me/inbox"))).GetProperty("items")[0].GetProperty("read").GetBoolean().Should().BeTrue();

        using var english = _hosts.ApiClient("en", await TokenOf(alice));
        (await Read(await english.GetAsync("/v1/me/inbox"))).GetProperty("items")[0].GetProperty("text").GetString().Should().Be("Your therapist suggestions are ready.");
    }

    [Fact]
    public async Task Quiet_hours_hold_the_copy_but_crisis_kinds_go_at_once()
    {
        var (client, patientId, email) = await PatientAsync("ar");
        await SetQuietHoursAsync(client, around: true);
        var before = _hosts.Outbox.Messages.Count(m => m.To == email);

        await Notifier.NotifyAsync(new Notification(patientId, NotificationKinds.TriageMatchesReady, new Dictionary<string, string>()), CancellationToken.None);
        _hosts.Outbox.Messages.Count(m => m.To == email).Should().Be(before, "quiet hours hold an ordinary copy");
        await using (var db = _sql.Context(StaticCurrentUser.ForPatient(patientId)))
        {
            var held = await db.Notifications.SingleAsync(n => n.RecipientUserId == patientId && n.Kind == NotificationKinds.TriageMatchesReady);
            held.External.Should().Be(ExternalDelivery.Pending);
            held.ExternalNotBeforeUtc.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(30));
        }

        // The worker leaves it alone until quiet hours end.
        await _hosts.Api.Services.GetRequiredService<NotificationWorker>().RunOnceAsync(CancellationToken.None);
        _hosts.Outbox.Messages.Count(m => m.To == email).Should().Be(before);

        await Notifier.NotifyAsync(new Notification(patientId, NotificationKinds.CrisisFollowUp, new Dictionary<string, string> { ["eventId"] = Guid.NewGuid().ToString() }), CancellationToken.None);
        _hosts.Outbox.Messages.Count(m => m.To == email).Should().Be(before + 1, "crisis kinds ignore quiet hours");
        _hosts.Outbox.LastTo(email)!.Text.Should().Be("نطمئن عليك. افتح التطبيق، ومعك من يساعدك متى احتجت.");

        // Once the window is over, the held copy goes out on the next run.
        await SetQuietHoursAsync(client, null);
        await using (var db = _sql.Context(StaticCurrentUser.ForStaff(Notifier.ServiceId, AppRoles.NotificationService)))
        {
            await db.Notifications.Where(n => n.RecipientUserId == patientId && n.External == ExternalDelivery.Pending)
                .ExecuteUpdateAsync(u => u.SetProperty(n => n.ExternalNotBeforeUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        await _hosts.Api.Services.GetRequiredService<NotificationWorker>().RunOnceAsync(CancellationToken.None);
        _hosts.Outbox.Messages.Count(m => m.To == email).Should().Be(before + 2);
    }

    [Fact]
    public async Task Switched_off_channels_get_no_copy_unless_the_kind_is_important()
    {
        var (client, patientId, email) = await PatientAsync("en");
        await Read(await client.PutAsJsonAsync("/v1/me/notifications", new { push = false, sms = false, email = false, neutralText = false, quickExit = false, homeworkReminder = "daily", reminderTime = "21:00", quietFrom = "00:00", quietTo = "00:00" }));
        var before = _hosts.Outbox.Messages.Count(m => m.To == email);
        await Notifier.NotifyAsync(new Notification(patientId, NotificationKinds.TriageMatchesReady, new Dictionary<string, string>()), CancellationToken.None);
        _hosts.Outbox.Messages.Count(m => m.To == email).Should().Be(before);

        await Read(await client.PutAsJsonAsync("/v1/me/notifications", new { push = false, sms = false, email = true, neutralText = true, quickExit = false, homeworkReminder = "daily", reminderTime = "21:00", quietFrom = "00:00", quietTo = "00:00" }));
        await Notifier.NotifyAsync(new Notification(patientId, NotificationKinds.HomeworkAssigned, new Dictionary<string, string>()), CancellationToken.None);
        _hosts.Outbox.LastTo(email)!.Text.Should().Be("You have a reminder.", "neutral notifications say nothing at all");
    }

    [Fact]
    public async Task Reminders_are_sent_once_across_runs_and_restarts_and_say_nothing_clinical()
    {
        var (client, patientId, email) = await PatientAsync("ar");
        await SetQuietHoursAsync(client, null);
        var therapists = await Read(await client.GetAsync("/v1/therapists"));
        var therapist = therapists.EnumerateArray().First();
        var therapistId = therapist.GetProperty("id").GetGuid();
        var therapistName = therapist.GetProperty("displayName").GetString()!;
        var slot = await AddSlotAsync(therapistId, DateTimeOffset.UtcNow.AddHours(5));
        var hold = await Read(await client.PostAsJsonAsync("/v1/bookings/holds", new { slotId = slot }));
        var appointment = await Read(await client.PostAsync($"/v1/bookings/holds/{hold.GetProperty("holdId").GetGuid()}/confirm", null));
        var appointmentId = appointment.GetProperty("id").GetGuid();

        var worker = _hosts.Api.Services.GetRequiredService<NotificationWorker>();
        await worker.RunOnceAsync(CancellationToken.None);
        await worker.RunOnceAsync(CancellationToken.None);
        // A restart: a second worker over the same database.
        var restarted = ActivatorUtilities.CreateInstance<NotificationWorker>(_hosts.Api.Services);
        await restarted.RunOnceAsync(CancellationToken.None);

        await using var db = _sql.Context(StaticCurrentUser.ForPatient(patientId));
        var reminders = await db.Notifications.Where(n => n.RecipientUserId == patientId && n.Kind == NotificationKinds.AppointmentReminder24h).ToListAsync();
        reminders.Should().ContainSingle();
        reminders[0].Link.Should().Be($"/appointments/{appointmentId}");
        var sms = _hosts.Outbox.Messages.Where(m => m.To == email && m.Text.StartsWith("تذكير", StringComparison.Ordinal)).ToList();
        sms.Should().ContainSingle();
        sms[0].Text.Should().NotContain(therapistName);
        sms[0].Text.Should().NotContainAny(ClinicalWords);

        // The in-app entry may name the therapist.
        var inbox = await Read(await client.GetAsync("/v1/me/inbox"));
        inbox.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("kind").GetString() == NotificationKinds.AppointmentReminder24h)
            .GetProperty("text").GetString().Should().Contain(therapistName);

        // The booking itself told both sides.
        inbox.GetProperty("items").EnumerateArray().Should().Contain(i => i.GetProperty("kind").GetString() == NotificationKinds.AppointmentBooked);
        await using var staffDb = _sql.Context(StaticCurrentUser.ForStaff(therapistId, AppRoles.Therapist));
        (await staffDb.Notifications.CountAsync(n => n.RecipientUserId == therapistId && n.Kind == NotificationKinds.StaffAppointmentBooked && n.DataJson.Contains(appointmentId.ToString()))).Should().Be(1);
    }

    [Fact]
    public void Every_sms_and_email_text_is_neutral_in_both_languages()
    {
        using var scope = _hosts.Api.Services.CreateScope();
        var texts = scope.ServiceProvider.GetRequiredService<NotificationTexts>();
        var data = new Dictionary<string, string>
        {
            ["startsAt"] = DateTimeOffset.UtcNow.AddDays(1).ToString("O", CultureInfo.InvariantCulture),
            ["therapistName"] = "Sara M.",
            ["therapist"] = "Sara M.",
            ["minutes"] = "10",
            ["pseudonymId"] = "P-ABCD-1234",
        };
        foreach (var kind in NotificationCatalog.Kinds)
        {
            foreach (var language in new[] { "ar", "en" })
            {
                var text = texts.External(kind.Code, data, language, "Asia/Riyadh", neutral: false);
                text.Should().NotContainAny(ClinicalWords, $"{kind.Code} ({language})");
                text.Should().NotContain("Sara").And.NotContain("P-ABCD");
                texts.InApp(kind.Code, data, language, "Asia/Riyadh").Should().NotContain("{", $"{kind.Code} in-app ({language})");
            }
        }

        // EC-52 and EC-53 reach supervisors with their own text, never the generic «لديك إشعار جديد.».
        var generic = texts.InApp("no.such.kind", data, "ar", "Asia/Riyadh");
        foreach (var kind in new[] { NotificationKinds.AppointmentRebookNeeded, NotificationKinds.AppointmentNoShowFollowUp })
        {
            foreach (var language in new[] { "ar", "en" })
            {
                var text = texts.InApp(kind, data, language, "Asia/Riyadh");
                text.Should().NotBe(generic).And.NotBe(texts.InApp("no.such.kind", data, language, "Asia/Riyadh")).And.Contain("P-ABCD-1234", kind);
                texts.InApp(kind, new Dictionary<string, string>(), language, "Asia/Riyadh").Should().NotBe(texts.InApp("no.such.kind", data, language, "Asia/Riyadh"), "the plain line stands in without data");
            }
        }
    }

    [Fact]
    public async Task A_closed_crisis_event_leads_to_the_follow_up_screen_and_tells_the_treating_therapist()
    {
        var (client, patientId, _) = await PatientAsync("ar");
        await SetQuietHoursAsync(client, null);
        (await Read(await client.GetAsync("/v1/crisis/follow-up"))).GetProperty("state").GetString().Should().Be("none");

        var therapist = await TherapistIdAsync("noura@staff.tamaneen.dev");
        await _sql.AssignAsync(patientId, therapist);
        var supervisor = await TherapistIdAsync("fahad@staff.tamaneen.dev");
        var pseudonym = (await Read(await client.GetAsync("/v1/me"))).GetProperty("pseudonymId").GetString()!;
        Guid eventId;
        await using (var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.CrisisService)))
        {
            var evt = CrisisEvent.Open(patientId, pseudonym, RiskLevel.High, "ar", "test", TimeSpan.FromMinutes(2), _sql.Clock);
            db.CrisisEvents.Add(evt);
            await db.SaveChangesAsync();
            eventId = evt.Id;
        }

        (await Read(await client.GetAsync("/v1/crisis/follow-up"))).GetProperty("state").GetString().Should().Be("open");
        await using (var db = _sql.Context(StaticCurrentUser.ForStaff(supervisor, AppRoles.Supervisor)))
        {
            var evt = await db.CrisisEvents.SingleAsync(e => e.Id == eventId);
            evt.Acknowledge(supervisor, _sql.Clock);
            evt.Close(supervisor, AppRoles.Supervisor, "safe_contacted", "Spoke with the patient; plan agreed.", _sql.Clock).Should().BeTrue();
            await db.SaveChangesAsync();
        }

        var worker = _hosts.Api.Services.GetRequiredService<NotificationWorker>();
        await worker.RunOnceAsync(CancellationToken.None);
        await worker.RunOnceAsync(CancellationToken.None);

        var followUp = await Read(await client.GetAsync("/v1/crisis/follow-up"));
        followUp.GetProperty("state").GetString().Should().Be("closed");
        followUp.GetProperty("unread").GetBoolean().Should().BeTrue();
        followUp.GetProperty("talked").GetBoolean().Should().BeTrue();
        followUp.GetProperty("responder").GetString().Should().Be("د. فهد ر.");
        followUp.GetProperty("therapistInformed").GetBoolean().Should().BeTrue();
        followUp.GetRawText().Should().NotContain("plan agreed", "the responder's close reason never reaches the patient");

        var inbox = await Read(await client.GetAsync("/v1/me/inbox"));
        inbox.GetProperty("items").EnumerateArray().Count(i => i.GetProperty("kind").GetString() == NotificationKinds.CrisisFollowUp).Should().Be(1);
        await using (var db = _sql.Context(StaticCurrentUser.ForStaff(therapist, AppRoles.Therapist)))
        {
            (await db.Notifications.CountAsync(n => n.RecipientUserId == therapist && n.Kind == NotificationKinds.CrisisClosed && n.DedupeKey == NotificationWorker.FollowUpKey(eventId))).Should().Be(1);
        }

        await Read(await client.PostAsync("/v1/crisis/follow-up/seen", null));
        (await Read(await client.GetAsync("/v1/crisis/follow-up"))).GetProperty("unread").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task A_sign_in_from_a_new_device_leaves_a_security_notice()
    {
        var email = $"nt-{Guid.NewGuid():N}@example.com";
        using var anon = _hosts.ApiClient("en");
        anon.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0) Safari/604.1");
        var eligibility = await Read(await anon.PostAsJsonAsync("/v1/auth/eligibility", new { isAdult = true, cityCode = "RUH" }));
        var challenge = await Read(await anon.PostAsJsonAsync("/v1/auth/signup", new { eligibilityToken = eligibility.GetProperty("eligibilityToken").GetString(), nickname = "Calm Palm", contactType = "email", contact = email, password = Password, language = "en" }));
        var verified = await Read(await anon.PostAsJsonAsync("/v1/auth/signup/verify", new { challengeId = challenge.GetProperty("challengeId").GetGuid(), code = _hosts.CodeSentTo(email) }));
        using var me = _hosts.ApiClient("en", verified.GetProperty("tokens").GetProperty("accessToken").GetString());

        using var other = _hosts.ApiClient("en");
        other.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0) Chrome/120.0 Safari/537.36");
        await Read(await other.PostAsJsonAsync("/v1/auth/login", new { contact = email, password = Password, client = "patient" }));

        var item = (await Read(await me.GetAsync("/v1/me/inbox"))).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("kind").GetString() == NotificationKinds.SecurityNewSignIn);
        item.GetProperty("text").GetString().Should().Be("New sign-in from Chrome · Windows. Not you? End the session.");
        item.GetProperty("data").GetProperty("sessionId").GetString().Should().NotBeNullOrEmpty();
        item.GetProperty("link").GetString().Should().Be("/settings/security");
    }

    // ---------- helpers ----------

    private static async Task<string> TokenOf(HttpClient client) => await Task.FromResult(client.DefaultRequestHeaders.Authorization!.Parameter!);

    private async Task<Guid> TherapistIdAsync(string email)
    {
        var token = await _hosts.StaffSignInAsync(email);
        using var staff = _hosts.ApiClient("ar", token);
        return (await Read(await staff.GetAsync("/v1/staff/me"))).GetProperty("userId").GetGuid();
    }

    /// <summary>around: a quiet window around now (Riyadh); null: no quiet hours at all.</summary>
    private static async Task SetQuietHoursAsync(HttpClient client, bool? around)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
        var now = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        var from = around == true ? now.AddHours(-1).ToString("HH:mm", CultureInfo.InvariantCulture) : "00:00";
        var to = around == true ? now.AddHours(2).ToString("HH:mm", CultureInfo.InvariantCulture) : "00:00";
        await Read(await client.PutAsJsonAsync("/v1/me/notifications", new { push = true, sms = false, email = false, neutralText = false, quickExit = false, homeworkReminder = "daily", reminderTime = "21:00", quietFrom = from, quietTo = to }));
    }

    private async Task<Guid> AddSlotAsync(Guid therapistId, DateTimeOffset startsAt)
    {
        await using var scope = _hosts.Api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestCurrentUser>().ActAsService(SchedulingEndpoints.SchedulingServiceId, AppRoles.SchedulingService);
        var db = scope.ServiceProvider.GetRequiredService<TamaneenDbContext>();
        var slot = AvailabilitySlot.Create(therapistId, new DateTimeOffset(startsAt.UtcDateTime.AddTicks(-(startsAt.UtcTicks % TimeSpan.TicksPerMinute)), TimeSpan.Zero), 50);
        db.AvailabilitySlots.Add(slot);
        await db.SaveChangesAsync();
        return slot.Id;
    }

    /// <summary>A signed-up patient (e-mail contact) who may book: consents, safety info, direct path, negative check.</summary>
    private async Task<(HttpClient Client, Guid PatientId, string Email)> PatientAsync(string language)
    {
        var email = $"nt-{Guid.NewGuid():N}@example.com";
        using var anon = _hosts.ApiClient(language);
        var eligibility = await Read(await anon.PostAsJsonAsync("/v1/auth/eligibility", new { isAdult = true, cityCode = "RUH" }));
        var challenge = await Read(await anon.PostAsJsonAsync("/v1/auth/signup", new { eligibilityToken = eligibility.GetProperty("eligibilityToken").GetString(), nickname = "Calm Palm", contactType = "email", contact = email, password = Password, language }));
        var verified = await Read(await anon.PostAsJsonAsync("/v1/auth/signup/verify", new { challengeId = challenge.GetProperty("challengeId").GetGuid(), code = _hosts.CodeSentTo(email) }));
        var me = _hosts.ApiClient(language, verified.GetProperty("tokens").GetProperty("accessToken").GetString());
        await Read(await me.PutAsJsonAsync("/v1/me/consents", new { items = new[] { "platform", "ai", "share_with_therapist" }.Select(k => new { key = k, granted = true }) }));
        await Read(await PutSafetyInfoAsync(me, new { shareLocation = false }));
        await Read(await me.PutAsJsonAsync("/v1/me/path", new { path = "direct" }));
        await Read(await me.PostAsJsonAsync("/v1/screening/cssrs", new { answers = new[] { false, false, false, false, false, false } }));
        var pseudonym = (await Read(await me.GetAsync("/v1/me"))).GetProperty("pseudonymId").GetString()!;
        await using var db = _sql.Context(StaticCurrentUser.ForStaff(Notifier.ServiceId, AppRoles.NotificationService));
        var id = await db.UserAccounts.Where(a => a.PseudonymId == pseudonym).Select(a => a.Id).SingleAsync();
        return (me, id, email);
    }
}
