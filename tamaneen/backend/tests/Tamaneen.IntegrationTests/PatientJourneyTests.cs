using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Tamaneen.IntegrationTests.JourneyHosts;

namespace Tamaneen.IntegrationTests;

/// <summary>
/// The whole patient journey through the real API and crisis worker on SQL Server, as the apps call it:
/// sign-up, consents, intake with questionnaires and AI summary, supervisor review, matching, booking,
/// therapist dashboard, homework, a crisis during a check-in and its handling on the console.
/// </summary>
[Collection(SqlServerGroup.Name)]
public sealed class PatientJourneyTests : IAsyncLifetime
{
    private readonly SqlServerFixture _sql;
    private JourneyHosts _hosts = null!;

    public PatientJourneyTests(SqlServerFixture sql) => _sql = sql;

    public Task InitializeAsync()
    {
        _hosts = new JourneyHosts(_sql);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _hosts.DisposeAsync();

    [Fact]
    public async Task An_arabic_patient_goes_from_sign_up_to_a_handled_crisis()
    {
        var phone = "+9665" + Random.Shared.Next(10000000, 99999999);
        using var anon = _hosts.ApiClient("ar");

        // P15 eligibility, localized pilot config.
        var config = await Read(await anon.GetAsync("/v1/meta/config"));
        config.GetProperty("pilotCities")[0].GetProperty("name").GetString().Should().Be("الرياض");
        var notAdult = await Read(await anon.PostAsJsonAsync("/v1/auth/eligibility", new { isAdult = false, cityCode = "RUH" }));
        notAdult.GetProperty("eligible").GetBoolean().Should().BeFalse();
        notAdult.GetProperty("reason").GetString().Should().Be("age");
        var eligibility = await Read(await anon.PostAsJsonAsync("/v1/auth/eligibility", new { isAdult = true, cityCode = "RUH" }));
        var eligibilityToken = eligibility.GetProperty("eligibilityToken").GetString();

        // P02 sign-up: weak password refused in Arabic, then a real one.
        var weak = await Problem(await anon.PostAsJsonAsync("/v1/auth/signup", new { eligibilityToken, nickname = "نخلة هادئة", contactType = "phone", contact = phone, password = "password1234", language = "ar" }), 400);
        weak.GetProperty("code").GetString().Should().Be("error.auth.weak_password");
        weak.GetProperty("detail").GetString().Should().Contain("12 حرفًا");
        var challenge = await Read(await anon.PostAsJsonAsync("/v1/auth/signup", new { eligibilityToken, nickname = "نخلة هادئة", contactType = "phone", contact = phone, password = "ظل-النخيل-هادئ-2026", language = "ar" }));
        challenge.GetProperty("maskedContact").GetString().Should().EndWith(phone[^4..]).And.NotContain(phone[..^4]);
        var challengeId = challenge.GetProperty("challengeId").GetGuid();
        _hosts.Outbox.LastTo(phone)!.Text.Should().Contain("رمز التحقق");

        // P16: a wrong code counts down, the right one signs in and returns ten recovery codes once.
        var wrong = await Problem(await anon.PostAsJsonAsync("/v1/auth/signup/verify", new { challengeId, code = "000000" }), 400);
        wrong.GetProperty("remainingAttempts").GetInt32().Should().Be(4);
        var verified = await Read(await anon.PostAsJsonAsync("/v1/auth/signup/verify", new { challengeId, code = _hosts.CodeSentTo(phone) }));
        verified.GetProperty("recoveryCodes").GetArrayLength().Should().Be(10);
        var token = verified.GetProperty("tokens").GetProperty("accessToken").GetString()!;
        using var me = _hosts.ApiClient("ar", token);

        var state = await Read(await me.GetAsync("/v1/me"));
        state.GetProperty("nickname").GetString().Should().Be("نخلة هادئة");
        state.GetProperty("onboarding").GetProperty("consentsDone").GetBoolean().Should().BeFalse();

        // P03: required consents are required.
        var missing = await Problem(await me.PutAsJsonAsync("/v1/me/consents", new { items = new[] { new { key = "platform", granted = true } } }), 403);
        missing.GetProperty("code").GetString().Should().Be("error.consent.missing");
        await Read(await me.PutAsJsonAsync("/v1/me/consents", new { items = new[] { "platform", "ai", "share_with_therapist" }.Select(k => new { key = k, granted = true }) }));

        // P21, P22.
        await Read(await PutSafetyInfoAsync(me, new { contactPhone = "0551234521", trustedName = "أختي نورة", trustedPhone = "0559876543", shareLocation = true }));
        await Read(await me.PutAsJsonAsync("/v1/me/path", new { path = "intake" }));

        // P04: greeting, two turns, then PHQ-9 one item at a time; everything in Arabic.
        var intake = await Read(await me.PostAsync("/v1/intake", null));
        var intakeId = intake.GetProperty("intakeId").GetGuid();
        intake.GetProperty("messages")[0].GetProperty("text").GetString().Should().StartWith("أهلًا بك");
        for (var i = 0; i < 2; i++)
        {
            intake = await Read(await me.PostAsJsonAsync($"/v1/intake/{intakeId}/messages", new { text = "أشعر بضغط كبير في الشغل وما أنام زين، أختي سارة تساعدني" }));
        }

        intake.GetProperty("messages").EnumerateArray().Last().GetProperty("text").GetString().Should().Be("شكرًا لأنك شاركتني هذا. كيف يؤثر ذلك على نومك هذه الأيام؟");
        _hosts.Model.Requests.Should().OnlyContain(r => !r.Messages.Any(m => m.Text.Contains("سارة", StringComparison.Ordinal)), "names never reach the model");
        var question = intake.GetProperty("pendingQuestion");
        question.GetProperty("instrument").GetString().Should().Be("phq9");
        question.GetProperty("text").GetString().Should().Contain("خلال الأسبوعين الماضيين");
        foreach (var (instrument, items) in new[] { ("phq9", 9), ("gad7", 7) })
        {
            for (var item = 1; item <= items; item++)
            {
                var value = instrument == "phq9" && item == 9 ? 0 : 1;
                intake = await Read(await me.PostAsJsonAsync($"/v1/intake/{intakeId}/answers", new { instrument, item, value = (int?)value }));
                intake.GetProperty("crisis").GetBoolean().Should().BeFalse();
            }
        }

        intake = await Read(await me.PostAsJsonAsync($"/v1/intake/{intakeId}/messages", new { text = "أتمنى أتعلم كيف أهدّي نفسي قبل النوم" }));
        intake.GetProperty("canFinish").GetBoolean().Should().BeTrue();

        // P05: AI summary in Arabic, patient edits one section, submits.
        var summary = await Read(await me.PostAsync($"/v1/intake/{intakeId}/complete", null));
        summary.GetProperty("generatedByAi").GetBoolean().Should().BeTrue();
        summary.GetProperty("sections").GetProperty("concern").GetProperty("text").GetString().Should().StartWith("تشعر بضغط");
        summary = await Read(await me.PutAsJsonAsync($"/v1/intake/{intakeId}/summary", new { sections = new Dictionary<string, string> { ["goals"] = "أن أنام أفضل وأعود للرياضة." } }));
        summary.GetProperty("sections").GetProperty("goals").GetProperty("editedByPatient").GetBoolean().Should().BeTrue();
        var triage = await Read(await me.PostAsync($"/v1/intake/{intakeId}/submit", null));
        triage.GetProperty("status").GetString().Should().Be("pending");
        (await Read(await me.GetAsync("/v1/matches"))).GetProperty("status").GetString().Should().Be("pending");
        (await Problem(await me.PostAsJsonAsync("/v1/bookings/holds", new { slotId = Guid.NewGuid() }), 403)).GetProperty("code").GetString().Should().Be("error.booking.not_eligible");

        // Supervisor reviews the file (TOTP sign-in) and the platform proposes therapists.
        var supervisorToken = await _hosts.StaffSignInAsync("fahad@staff.tamaneen.dev");
        using var supervisor = _hosts.ApiClient("ar", supervisorToken);
        var queue = await Read(await supervisor.GetAsync("/v1/staff/triage"));
        var pseudonym = state.GetProperty("pseudonymId").GetString();
        var review = queue.EnumerateArray().Single(t => t.GetProperty("pseudonymId").GetString() == pseudonym);
        await Read(await supervisor.PostAsJsonAsync($"/v1/staff/triage/{review.GetProperty("id").GetGuid()}/review", new { riskLevel = 1, focus = new[] { "anxiety", "sleep", "stress" } }));

        // P07: Sara is the best match, explained by shared focus areas and language.
        var matches = await Read(await me.GetAsync("/v1/matches"));
        matches.GetProperty("status").GetString().Should().Be("ready");
        var best = matches.GetProperty("items")[0];
        best.GetProperty("best").GetBoolean().Should().BeTrue();
        best.GetProperty("therapist").GetProperty("displayName").GetString().Should().Be("أ. سارة م.");
        best.GetProperty("therapist").GetProperty("initials").GetString().Should().Be("س م");
        best.GetProperty("reasons").GetProperty("sharedSpecialties").EnumerateArray().Select(e => e.GetString()).Should().Contain(["anxiety", "sleep"]);
        best.GetProperty("reasons").GetProperty("speaksYourLanguage").GetBoolean().Should().BeTrue();
        var saraId = best.GetProperty("therapist").GetProperty("id").GetGuid();

        // P08: hold, then confirm; a second hold on the same slot by someone else would fail.
        var slots = await Read(await me.GetAsync($"/v1/therapists/{saraId}/slots?days=14"));
        var slotId = slots[0].GetProperty("slotId").GetGuid();
        var hold = await Read(await me.PostAsJsonAsync("/v1/bookings/holds", new { slotId }));
        DateTimeOffset.Parse(hold.GetProperty("expiresAt").GetString()!, System.Globalization.CultureInfo.InvariantCulture).Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(30));
        var appointment = await Read(await me.PostAsync($"/v1/bookings/holds/{hold.GetProperty("holdId").GetGuid()}/confirm", null));
        appointment.GetProperty("sequenceNumber").GetInt32().Should().Be(1);
        appointment.GetProperty("joinUrl").ValueKind.Should().Be(JsonValueKind.Null, "the link appears only ten minutes before");
        var appointmentId = appointment.GetProperty("id").GetGuid();
        var ics = await me.GetAsync($"/v1/appointments/{appointmentId}/calendar");
        ics.Content.Headers.ContentType!.MediaType.Should().Be("text/calendar");
        ics.Content.Headers.ContentDisposition!.FileName.Should().Be("appointment.ics");
        var icsText = await ics.Content.ReadAsStringAsync();
        icsText.Should().Contain("BEGIN:VEVENT").And.Contain("SUMMARY:موعد\r\n").And.Contain($"UID:{appointmentId:N}\r\n");
        icsText.Should().NotContainAny(["طمأنينة", "Tamaneen", "tamaneen", "سارة"], "the calendar entry is neutral (PB-42)");
        (await Read(await me.GetAsync("/v1/appointments"))).GetProperty("upcoming").GetArrayLength().Should().Be(1);
        (await Read(await me.GetAsync("/v1/triage"))).GetProperty("status").GetString().Should().Be("matched");

        // S01: Sara sees the new file waiting for her approval, opens it (audited) and approves it.
        var saraToken = await _hosts.StaffSignInAsync("sara@staff.tamaneen.dev");
        using var sara = _hosts.ApiClient("ar", saraToken);
        var dashboard = await Read(await sara.GetAsync("/v1/staff/dashboard"));
        dashboard.GetProperty("displayName").GetString().Should().Be("أ. سارة م.");
        dashboard.GetProperty("pendingProfiles").EnumerateArray().Should().Contain(p => p.GetProperty("pseudonymId").GetString() == pseudonym);
        var file = await Read(await sara.GetAsync($"/v1/staff/patients/{pseudonym}/intake"));
        file.GetProperty("phq9").GetInt32().Should().Be(8);
        file.GetProperty("gad7").GetInt32().Should().Be(7);
        file.GetProperty("sections").GetProperty("goals").GetString().Should().Be("أن أنام أفضل وأعود للرياضة.");
        await Read(await sara.PostAsync($"/v1/staff/patients/{pseudonym}/intake/approve", null));

        // Another therapist cannot see this patient at all (row-level security).
        var nouraToken = await _hosts.StaffSignInAsync("noura@staff.tamaneen.dev");
        using var noura = _hosts.ApiClient("ar", nouraToken);
        await Problem(await noura.GetAsync($"/v1/staff/patients/{pseudonym}/intake"), 404);

        // P10: Sara assigns homework; the patient checks in; Sara replies; P09 home shows it.
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh")).DateTime);
        await Read(await sara.PostAsJsonAsync($"/v1/staff/patients/{pseudonym}/homework", new { title = "تمرين التنفس قبل النوم", instructions = new[] { "اجلس في مكان هادئ.", "شهيق 4 ثوانٍ وزفير 6 ثوانٍ." }, minutes = 5, frequency = "daily", startsOn = today, endsOn = today.AddDays(6) }));
        var homework = await Read(await me.GetAsync("/v1/homework"));
        var homeworkId = homework.GetProperty("active")[0].GetProperty("id").GetGuid();
        homework.GetProperty("active")[0].GetProperty("dueToday").GetBoolean().Should().BeTrue();
        var checkin = await Read(await me.PostAsJsonAsync($"/v1/homework/{homeworkId}/checkins", new { status = "done", feeling = 7, note = "نمت أسرع من الأمس." }));
        checkin.GetProperty("crisis").GetBoolean().Should().BeFalse();
        checkin.GetProperty("homework").GetProperty("doneDays").GetInt32().Should().Be(1);
        await using (var db = _sql.Context(Tamaneen.SharedKernel.StaticCurrentUser.ForStaff(Guid.NewGuid(), Tamaneen.SharedKernel.AppRoles.Supervisor)))
        {
            var checkinId = db.HomeworkCheckins.Single(c => c.AssignmentId == homeworkId).Id;
            await Read(await sara.PostAsJsonAsync($"/v1/staff/checkins/{checkinId}/feedback", new { text = "أحسنت في الاستمرار." }));
        }

        var home = await Read(await me.GetAsync("/v1/home"));
        home.GetProperty("nextAppointment").GetProperty("id").GetGuid().Should().Be(appointmentId);
        home.GetProperty("latestFeedback").GetProperty("text").GetString().Should().Be("أحسنت في الاستمرار.");
        home.GetProperty("todayHomework").ValueKind.Should().Be(JsonValueKind.Null, "today's check-in is done");

        // A crisis in a mood note: the gate locks the chat before anything else happens.
        var mood = await Read(await me.PostAsJsonAsync("/v1/mood", new { score = 1, tags = new[] { "tired" }, note = "ما عاد أتحمل، أبي أموت الليلة" }));
        mood.GetProperty("crisis").GetBoolean().Should().BeTrue();
        mood.GetProperty("script").GetString().Should().StartWith("أنت لست وحدك");
        (await Read(await me.GetAsync("/v1/me"))).GetProperty("chatLocked").GetBoolean().Should().BeTrue();
        var status = await Read(await me.GetAsync("/v1/crisis/status"));
        status.GetProperty("eventOpen").GetBoolean().Should().BeTrue();
        status.GetProperty("contactPhoneLast4").GetString().Should().Be("4521");
        var locked = await Problem(await me.GetAsync($"/v1/therapists/{saraId}/slots"), 409);
        locked.GetProperty("code").GetString().Should().Be("error.crisis.locked");

        // S04: Sara (assigned) sees the event, with patient words and contact data hidden until she verifies again.
        using var saraConsole = _hosts.WorkerClient("ar", saraToken);
        var open = await Read(await saraConsole.GetAsync("/v1/crisis/events/open"));
        var evt = open.EnumerateArray().Single(e => e.GetProperty("pseudonymId").GetString() == pseudonym);
        evt.GetProperty("number").GetString().Should().StartWith("CE-");
        evt.GetProperty("sourceLabel").GetString().Should().Be("تسجيل المزاج");
        var eventId = evt.GetProperty("eventId").GetGuid();
        var detail = await Read(await saraConsole.GetAsync($"/v1/crisis/events/{eventId}"));
        detail.GetProperty("maskedPhone").GetString().Should().Be("••• ••• ••21");
        detail.GetProperty("hasTrustedContact").GetBoolean().Should().BeTrue();
        detail.GetProperty("canClose").GetBoolean().Should().BeFalse();
        await Problem(await saraConsole.PostAsJsonAsync($"/v1/crisis/events/{eventId}/reveal-contact", new { totpCode = "000000", reason = "اتصال طارئ" }), 403);
        var contact = await Read(await saraConsole.PostAsJsonAsync($"/v1/crisis/events/{eventId}/reveal-contact", new { totpCode = StaffTotp("sara@staff.tamaneen.dev"), reason = "اتصال طارئ" }));
        contact.GetProperty("contactPhone").GetString().Should().Be("0551234521");
        contact.GetProperty("trustedName").GetString().Should().Be("أختي نورة");
        var excerpt = await Read(await saraConsole.PostAsJsonAsync($"/v1/crisis/events/{eventId}/excerpt", new { totpCode = StaffTotp("sara@staff.tamaneen.dev") }));
        excerpt.GetProperty("excerpt").GetString().Should().Be("ما عاد أتحمل، أبي أموت الليلة");
        await Read(await saraConsole.PostAsync($"/v1/crisis/events/{eventId}/acknowledge", null));
        await Read(await saraConsole.PostAsJsonAsync($"/v1/crisis/events/{eventId}/actions", new { kind = "contacted", text = "ردّت على الجوال وهي بخير." }));
        detail = await Read(await saraConsole.PostAsJsonAsync($"/v1/crisis/events/{eventId}/actions", new { kind = "note", text = "تحدثت معها وهي في أمان مع أختها." }));
        var timeline = detail.GetProperty("timeline").EnumerateArray().Select(t => t.GetProperty("kind").GetString()).ToList();
        timeline.Should().ContainInOrder("opened", "contact_revealed", "excerpt_viewed", "acknowledged", "contacted", "note");
        detail.GetProperty("timeline").EnumerateArray().Last().GetProperty("text").GetString().Should().Contain("في أمان");

        // Noura (not assigned, not on call) does not see the event.
        using var nouraConsole = _hosts.WorkerClient("ar", nouraToken);
        (await Read(await nouraConsole.GetAsync("/v1/crisis/events/open"))).EnumerateArray().Should().NotContain(e => e.GetProperty("eventId").GetGuid() == eventId);
        await Problem(await nouraConsole.GetAsync($"/v1/crisis/events/{eventId}"), 404);

        // Khalid is on call: not her therapist, but paged, so he can open the event and reach her.
        using var khalidConsole = _hosts.WorkerClient("ar", await _hosts.StaffSignInAsync("khalid@staff.tamaneen.dev"));
        (await Read(await khalidConsole.GetAsync("/v1/crisis/events/open"))).EnumerateArray().Should().Contain(e => e.GetProperty("eventId").GetGuid() == eventId);
        (await Read(await khalidConsole.GetAsync($"/v1/crisis/events/{eventId}"))).GetProperty("maskedPhone").GetString().Should().Be("••• ••• ••21");
        (await Read(await khalidConsole.PostAsJsonAsync($"/v1/crisis/events/{eventId}/reveal-contact", new { totpCode = StaffTotp("khalid@staff.tamaneen.dev"), reason = "مناوبة" })))
            .GetProperty("contactPhone").GetString().Should().Be("0551234521");

        // The supervisor closes it with an outcome; the chat opens again; the access log shows who looked.
        using var supervisorConsole = _hosts.WorkerClient("ar", supervisorToken);
        await Read(await supervisorConsole.PostAsJsonAsync($"/v1/crisis/events/{eventId}/close", new { outcome = "safe_contacted", reason = "تواصلنا معها وهي في أمان." }));
        (await Read(await me.GetAsync("/v1/me"))).GetProperty("chatLocked").GetBoolean().Should().BeFalse();
        var log = await Read(await me.GetAsync("/v1/me/access-log"));
        log.EnumerateArray().Select(e => e.GetProperty("action").GetString()).Should().Contain(["patient.file_opened", "crisis.contact_revealed", "crisis.excerpt_viewed", "triage.reviewed"]);
        log.EnumerateArray().Should().Contain(e => e.GetProperty("actor").GetString() == "therapist");
    }

    [Fact]
    public async Task Sessions_rotate_detect_reuse_and_end_at_sign_out()
    {
        var email = $"journey-{Guid.NewGuid():N}@example.com";
        var (access, refresh) = await SignUpAsync("en", email);
        using var me = _hosts.ApiClient("en", access);
        await Read(await me.GetAsync("/v1/me"));

        using var anon = _hosts.ApiClient("en");
        var rotated = await Read(await anon.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = refresh }));
        var newRefresh = rotated.GetProperty("refreshToken").GetString();
        newRefresh.Should().NotBe(refresh);

        // The old refresh token again: treated as stolen, the whole session ends at once.
        var reuse = await Problem(await anon.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = refresh }), 401);
        reuse.GetProperty("code").GetString().Should().Be("error.auth.session_revoked");
        reuse.GetProperty("detail").GetString().Should().Be("Your session ended to protect you. Please sign in again.");
        await Problem(await anon.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = newRefresh }), 401);
        (await me.GetAsync("/v1/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the access token's session was revoked");

        // Sign in again, then sign out: the token stops working immediately.
        var login = await Read(await anon.PostAsJsonAsync("/v1/auth/login", new { contact = email, password = "Quiet-Morning-Walk-77" }));
        login.GetProperty("status").GetString().Should().Be("ok");
        var tokens = login.GetProperty("tokens");
        using var again = _hosts.ApiClient("en", tokens.GetProperty("accessToken").GetString());
        await Read(await again.GetAsync("/v1/me"));
        (await anon.PostAsJsonAsync("/v1/auth/logout", new { refreshToken = tokens.GetProperty("refreshToken").GetString() })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await again.GetAsync("/v1/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sign_in_errors_are_localized_count_down_and_lock_without_revealing_accounts()
    {
        var email = $"lock-{Guid.NewGuid():N}@example.com";
        await SignUpAsync("ar", email);
        using var ar = _hosts.ApiClient("ar");
        using var en = _hosts.ApiClient("en");

        var first = await Problem(await ar.PostAsJsonAsync("/v1/auth/login", new { contact = email, password = "wrong-password-1" }), 401);
        first.GetProperty("remainingAttempts").GetInt32().Should().Be(4);
        first.GetProperty("detail").GetString().Should().Be("البيانات غير صحيحة.");

        // An unknown account answers the same way, with the same countdown.
        var unknown = await Problem(await en.PostAsJsonAsync("/v1/auth/login", new { contact = $"nobody-{Guid.NewGuid():N}@example.com", password = "wrong-password-1" }), 401);
        unknown.GetProperty("remainingAttempts").GetInt32().Should().Be(4);
        unknown.GetProperty("detail").GetString().Should().Be("The details are not correct.");

        for (var i = 0; i < 3; i++)
        {
            await Problem(await ar.PostAsJsonAsync("/v1/auth/login", new { contact = email, password = "wrong-password-1" }), 401);
        }

        var lockedOut = await Problem(await en.PostAsJsonAsync("/v1/auth/login", new { contact = email, password = "wrong-password-1" }), 423);
        lockedOut.GetProperty("retryAfterSeconds").GetInt32().Should().BeInRange(890, 900);
        lockedOut.GetProperty("detail").GetString().Should().Be("We paused sign-in to protect you. Try again in 15 minutes.");

        // Even the right password waits out the lock.
        await Problem(await en.PostAsJsonAsync("/v1/auth/login", new { contact = email, password = "Quiet-Morning-Walk-77" }), 423);

        // Recovery always answers the same, whether or not the account exists.
        var real = await Read(await en.PostAsJsonAsync("/v1/auth/recover", new { contact = email }));
        var fake = await Read(await en.PostAsJsonAsync("/v1/auth/recover", new { contact = $"ghost-{Guid.NewGuid():N}@example.com" }));
        real.EnumerateObject().Select(p => p.Name).Should().Equal(fake.EnumerateObject().Select(p => p.Name));
        await Read(await en.PostAsJsonAsync("/v1/auth/recover/complete", new { challengeId = real.GetProperty("challengeId").GetGuid(), code = _hosts.CodeSentTo(email), newPassword = "New-Calm-River-2026" }));
        var after = await Read(await en.PostAsJsonAsync("/v1/auth/login", new { contact = email, password = "New-Calm-River-2026" }));
        after.GetProperty("status").GetString().Should().Be("ok", "recovery resets the lock");
    }

    [Fact]
    public async Task An_english_patient_on_the_direct_path_is_answered_in_english_and_help_now_pages_the_team()
    {
        var (access, _) = await SignUpAsync("en", $"direct-{Guid.NewGuid():N}@example.com");
        using var me = _hosts.ApiClient("en", access);
        await Read(await me.PutAsJsonAsync("/v1/me/consents", new { items = new[] { "platform", "ai", "share_with_therapist", "research" }.Select(k => new { key = k, granted = true }) }));
        await Read(await PutSafetyInfoAsync(me, new { shareLocation = false }));
        await Read(await me.PutAsJsonAsync("/v1/me/path", new { path = "direct" }));

        // P23: questions arrive in English; a clear result unlocks booking.
        var form = await Read(await me.GetAsync("/v1/screening/cssrs"));
        form.GetProperty("questions")[0].GetProperty("text").GetString().Should().StartWith("Have you wished you were dead");
        form.GetProperty("note").GetString().Should().Be("We ask everyone these questions to make sure you are safe. There are no wrong answers.");
        var clear = await Read(await me.PostAsJsonAsync("/v1/screening/cssrs", new { answers = new bool[6] }));
        clear.GetProperty("crisis").GetBoolean().Should().BeFalse();

        // P24: English directory with filters.
        var directory = await Read(await me.GetAsync("/v1/therapists?specialty=anxiety&gender=male"));
        directory.EnumerateArray().Should().ContainSingle().Which.GetProperty("displayName").GetString().Should().Be("Khalid A.");
        var profile = await Read(await me.GetAsync($"/v1/therapists/{directory[0].GetProperty("id").GetGuid()}"));
        profile.GetProperty("bio").GetString().Should().StartWith("I help people");
        var slots = await Read(await me.GetAsync($"/v1/therapists/{directory[0].GetProperty("id").GetGuid()}/slots"));
        var hold = await Read(await me.PostAsJsonAsync("/v1/bookings/holds", new { slotId = slots[0].GetProperty("slotId").GetGuid() }));
        await Read(await me.PostAsync($"/v1/bookings/holds/{hold.GetProperty("holdId").GetGuid()}/confirm", null));

        // Khalid sees the direct-path file waiting; approving it is the clinical review and it leaves the list.
        var pseudonym = (await Read(await me.GetAsync("/v1/me"))).GetProperty("pseudonymId").GetString();
        using var khalid = _hosts.ApiClient("en", await _hosts.StaffSignInAsync("khalid@staff.tamaneen.dev"));
        (await Read(await khalid.GetAsync("/v1/staff/dashboard"))).GetProperty("pendingProfiles").EnumerateArray().Should().Contain(p => p.GetProperty("pseudonymId").GetString() == pseudonym);
        (await Read(await khalid.GetAsync($"/v1/staff/patients/{pseudonym}/intake"))).GetProperty("cssrsPositive").GetBoolean().Should().BeFalse();
        await Read(await khalid.PostAsync($"/v1/staff/patients/{pseudonym}/intake/approve", null));
        (await Read(await khalid.GetAsync($"/v1/staff/patients/{pseudonym}/intake"))).GetProperty("approved").GetBoolean().Should().BeTrue();
        (await Read(await khalid.GetAsync("/v1/staff/dashboard"))).GetProperty("pendingProfiles").EnumerateArray().Should().NotContain(p => p.GetProperty("pseudonymId").GetString() == pseudonym);

        // Help now: a Critical event even without words, with the English script.
        var help = await Read(await me.PostAsync("/v1/crisis/help", null));
        help.GetProperty("crisis").GetBoolean().Should().BeTrue();
        help.GetProperty("level").GetInt32().Should().Be(4);
        help.GetProperty("script").GetString().Should().StartWith("You are not alone");
        var status = await Read(await me.GetAsync("/v1/crisis/status"));
        status.GetProperty("locked").GetBoolean().Should().BeTrue();
        status.GetProperty("script").GetString().Should().Contain("911");

        // The crisis raises the risk shown on the therapist's dashboard for this patient.
        (await Read(await khalid.GetAsync($"/v1/staff/patients/{pseudonym}/intake"))).GetProperty("riskLevel").GetInt32().Should().Be(4);

        // A locked chat refuses AI-assisted work with an English explanation.
        var locked = await Problem(await me.PostAsJsonAsync("/v1/bookings/holds", new { slotId = slots[1].GetProperty("slotId").GetGuid() }), 409);
        locked.GetProperty("detail").GetString().Should().StartWith("The chat is paused now.");
    }

    [Fact]
    public async Task A_patient_cancels_with_a_reason_and_the_calendar_entry_is_neutral()
    {
        var (access, _) = await SignUpAsync("en", $"cancel-{Guid.NewGuid():N}@example.com");
        using var me = _hosts.ApiClient("en", access);
        await Read(await me.PutAsJsonAsync("/v1/me/consents", new { items = new[] { "platform", "ai", "share_with_therapist" }.Select(k => new { key = k, granted = true }) }));
        await Read(await PutSafetyInfoAsync(me, new { shareLocation = false }));
        await Read(await me.PutAsJsonAsync("/v1/me/path", new { path = "direct" }));
        await Read(await me.PostAsJsonAsync("/v1/screening/cssrs", new { answers = new bool[6] }));

        // A time well past the 24-hour change deadline, from the end of the list so other tests keep the early ones.
        var therapistId = (await Read(await me.GetAsync("/v1/therapists")))[0].GetProperty("id").GetGuid();
        var slots = await Read(await me.GetAsync($"/v1/therapists/{therapistId}/slots?days=14"));
        var slot = slots.EnumerateArray().Last(s => DateTimeOffset.Parse(s.GetProperty("startsAt").GetString()!, System.Globalization.CultureInfo.InvariantCulture) > DateTimeOffset.UtcNow.AddDays(2));
        var hold = await Read(await me.PostAsJsonAsync("/v1/bookings/holds", new { slotId = slot.GetProperty("slotId").GetGuid() }));
        var id = (await Read(await me.PostAsync($"/v1/bookings/holds/{hold.GetProperty("holdId").GetGuid()}/confirm", null))).GetProperty("id").GetGuid();

        // PB-42: the English calendar entry is just "Appointment".
        var ics = await (await me.GetAsync($"/v1/appointments/{id}/calendar")).Content.ReadAsStringAsync();
        ics.Should().Contain("SUMMARY:Appointment\r\n").And.NotContain("Tamaneen").And.NotContain("tamaneen");

        // PB-45: a reason is required and must come from the list; the error is a localized field error.
        var missing = await Problem(await me.PostAsync($"/v1/appointments/{id}/cancel", null), 400);
        missing.GetProperty("errors").GetProperty("reasonCode")[0].GetString().Should().Be("This field is required.");
        var unknown = await Problem(await me.PostAsJsonAsync($"/v1/appointments/{id}/cancel", new { reasonCode = "bored" }), 400);
        unknown.GetProperty("errors").GetProperty("reasonCode")[0].GetString().Should().Be("This value is not valid.");
        (await Read(await me.GetAsync($"/v1/appointments/{id}"))).GetProperty("status").GetString().Should().Be("confirmed");

        var cancelled = await Read(await me.PostAsJsonAsync($"/v1/appointments/{id}/cancel", new { reasonCode = "schedule_conflict" }));
        cancelled.GetProperty("status").GetString().Should().Be("cancelled_by_patient");
    }

    [Fact]
    public async Task Sensitive_security_changes_need_the_password_and_the_authenticator_code()
    {
        var (access, _) = await SignUpAsync("ar", $"reauth-{Guid.NewGuid():N}@example.com");
        using var me = _hosts.ApiClient("ar", access);
        const string password = "Quiet-Morning-Walk-77";

        // Steps are handed out in order: each one is accepted once (replay protection), all within ±1 of now.
        var setup = await Read(await me.PostAsync("/v1/me/totp/setup", null));
        var secret = FromBase32(setup.GetProperty("secret").GetString()!);
        var step = Tamaneen.Infrastructure.Auth.Totp.StepAt(DateTimeOffset.UtcNow) - 1;
        string NextCode() => Tamaneen.Infrastructure.Auth.Totp.Code(secret, step++);
        (await me.PostAsJsonAsync("/v1/me/totp/confirm", new { code = NextCode() })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // PB-55: new recovery codes need the password, and the code while the authenticator is on.
        (await Problem(await me.PostAsync("/v1/me/recovery-codes", null), 400)).GetProperty("code").GetString().Should().Be("error.auth.password_mismatch");
        var noCode = await Problem(await me.PostAsJsonAsync("/v1/me/recovery-codes", new { password }), 400);
        noCode.GetProperty("code").GetString().Should().Be("error.auth.mfa_failed");
        noCode.GetProperty("detail").GetString().Should().Be("رمز التحقق غير صحيح.");
        (await Read(await me.PostAsJsonAsync("/v1/me/recovery-codes", new { password, code = NextCode() }))).GetProperty("recoveryCodes").GetArrayLength().Should().Be(10);

        // Turning the authenticator off needs the password as well as the code.
        var code = NextCode();
        var withoutPassword = await me.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/v1/me/totp") { Content = JsonContent.Create(new { code }) });
        (await Problem(withoutPassword, 400)).GetProperty("code").GetString().Should().Be("error.auth.password_mismatch");
        (await Read(await me.GetAsync("/v1/me/security"))).GetProperty("totpEnabled").GetBoolean().Should().BeTrue();
        var disabled = await me.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/v1/me/totp") { Content = JsonContent.Create(new { password, code }) });
        disabled.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // With the authenticator off, the password alone is enough.
        (await Read(await me.PostAsJsonAsync("/v1/me/recovery-codes", new { password }))).GetProperty("recoveryCodes").GetArrayLength().Should().Be(10);
    }

    private static byte[] FromBase32(string text)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in text.Replace(" ", string.Empty, StringComparison.Ordinal))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. bytes];
    }

    private async Task<(string Access, string Refresh)> SignUpAsync(string language, string email)
    {
        using var anon = _hosts.ApiClient(language);
        var eligibility = await Read(await anon.PostAsJsonAsync("/v1/auth/eligibility", new { isAdult = true, cityCode = "RUH" }));
        var challenge = await Read(await anon.PostAsJsonAsync("/v1/auth/signup", new { eligibilityToken = eligibility.GetProperty("eligibilityToken").GetString(), nickname = "Calm Palm", contactType = "email", contact = email, password = "Quiet-Morning-Walk-77", language }));
        var verified = await Read(await anon.PostAsJsonAsync("/v1/auth/signup/verify", new { challengeId = challenge.GetProperty("challengeId").GetGuid(), code = _hosts.CodeSentTo(email) }));
        var tokens = verified.GetProperty("tokens");
        return (tokens.GetProperty("accessToken").GetString()!, tokens.GetProperty("refreshToken").GetString()!);
    }
}
