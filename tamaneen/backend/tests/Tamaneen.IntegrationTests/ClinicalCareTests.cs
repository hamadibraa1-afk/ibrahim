using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tamaneen.SharedKernel;
using static Tamaneen.IntegrationTests.JourneyHosts;

namespace Tamaneen.IntegrationTests;

/// <summary>
/// Phase 5 care through the real API, crisis worker and SQL Server row-level security: homework (S16 with P10/P29),
/// treatment plan (S15), session notes (S33), the shared safety plan (S35/P37), end of care (S57/P53) and changing
/// therapist (P44 through the S03 queue), with the notifications each one hands over.
/// </summary>
[Collection(SqlServerGroup.Name)]
public sealed class ClinicalCareTests : IAsyncLifetime
{
    private const string Password = "Quiet-Morning-Walk-77";
    private readonly SqlServerFixture _sql;
    private JourneyHosts _hosts = null!;

    public ClinicalCareTests(SqlServerFixture sql) => _sql = sql;

    public Task InitializeAsync()
    {
        _hosts = new JourneyHosts(_sql);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _hosts.DisposeAsync();

    [Fact]
    public async Task Homework_is_assigned_checked_in_answered_edited_and_cancelled_end_to_end()
    {
        var patient = await BookedPatientAsync("أ. سارة م.");
        using var sara = _hosts.ApiClient("ar", await _hosts.StaffSignInAsync("sara@staff.tamaneen.dev"));
        var templates = await Read(await sara.GetAsync("/v1/staff/homework/templates"));
        templates.EnumerateArray().Select(t => t.GetProperty("code").GetString()).Should().Contain(["behavioral_activation", "thought_record", "sleep_diary", "breathing"]);
        var breathing = templates.EnumerateArray().Single(t => t.GetProperty("code").GetString() == "breathing");

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        await Problem(await sara.PostAsJsonAsync($"/v1/staff/patients/{patient.Pseudonym}/homework", new { title = new string('x', 121), instructions = new[] { "x" }, frequency = "daily", startsOn = today, endsOn = today.AddDays(6) }), 400);
        await Problem(await sara.PostAsJsonAsync($"/v1/staff/patients/{patient.Pseudonym}/homework", new { title = "x", instructions = new[] { "x" }, startsOn = today, endsOn = today.AddDays(6) }), 400);
        var created = await Read(await sara.PostAsJsonAsync($"/v1/staff/patients/{patient.Pseudonym}/homework", new
        {
            title = breathing.GetProperty("title").GetString(),
            instructions = breathing.GetProperty("instructions").EnumerateArray().Select(i => i.GetString()).ToArray(),
            minutes = 5,
            frequency = "daily",
            startsOn = today.AddDays(1), // a therapist's browser a day ahead of the patient: ignored
            endsOn = today.AddDays(6),
            templateCode = "breathing",
        }));
        var homeworkId = created.GetProperty("id").GetGuid();
        created.GetProperty("startsOn").GetString().Should().Be(today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), "the exercise starts on the patient's today");
        created.GetProperty("status").GetString().Should().Be("active");
        created.GetProperty("weeklyTarget").GetInt32().Should().Be(7);
        _hosts.Notifier.Sent.Should().Contain(n => n.Kind == "homework.assigned" && n.RecipientUserId == patient.PatientId && n.Data["homeworkId"] == homeworkId.ToString());

        // P29 and P10: the patient sees it and checks in with a note (through the crisis gate).
        var list = await Read(await patient.Me.GetAsync("/v1/homework"));
        list.GetProperty("active").EnumerateArray().Should().Contain(h => h.GetProperty("id").GetGuid() == homeworkId);
        await Read(await patient.Me.PostAsJsonAsync($"/v1/homework/{homeworkId}/checkins", new { status = "done", feeling = 7, note = "نمت أسرع من الأمس" }));
        (await Read(await patient.Me.GetAsync("/v1/homework"))).GetProperty("active").EnumerateArray().Single(h => h.GetProperty("id").GetGuid() == homeworkId)
            .GetProperty("doneThisPeriod").GetInt32().Should().Be(1);

        // S16: the check-in with the note, then a reply.
        var staffList = await Read(await sara.GetAsync($"/v1/staff/patients/{patient.Pseudonym}/homework"));
        var item = staffList.GetProperty("current").EnumerateArray().Single(h => h.GetProperty("id").GetGuid() == homeworkId);
        item.GetProperty("doneThisWeek").GetInt32().Should().Be(1);
        item.GetProperty("averageFeeling").GetDouble().Should().Be(7);
        var checkin = item.GetProperty("latestCheckin");
        checkin.GetProperty("note").GetString().Should().Be("نمت أسرع من الأمس");
        checkin.GetProperty("gateLevel").GetInt32().Should().BeLessThan(3);
        (await sara.PostAsJsonAsync($"/v1/staff/checkins/{checkin.GetProperty("id").GetGuid()}/feedback", new { text = "أحسنت في الاستمرار." })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        _hosts.Notifier.Sent.Should().Contain(n => n.Kind == "homework.feedback" && n.RecipientUserId == patient.PatientId);
        var detail = await Read(await patient.Me.GetAsync($"/v1/homework/{homeworkId}"));
        detail.GetProperty("latestFeedback").GetProperty("text").GetString().Should().Be("أحسنت في الاستمرار.");
        (await Read(await patient.Me.GetAsync("/v1/home"))).GetProperty("latestFeedback").GetProperty("text").GetString().Should().Be("أحسنت في الاستمرار.");

        // Edit: the patient sees the new title at once.
        var edited = await Read(await sara.PutAsJsonAsync($"/v1/staff/homework/{homeworkId}", new { title = "تنفس هادئ", instructions = new[] { "تنفس ببطء خمس دقائق." }, frequency = "twice_weekly", endsOn = today.AddDays(10) }));
        edited.GetProperty("weeklyTarget").GetInt32().Should().Be(2);
        (await Read(await patient.Me.GetAsync($"/v1/homework/{homeworkId}"))).GetProperty("title").GetString().Should().Be("تنفس هادئ");

        // Another therapist reaches nothing (row-level security).
        using var noura = _hosts.ApiClient("ar", await _hosts.StaffSignInAsync("noura@staff.tamaneen.dev"));
        await Problem(await noura.GetAsync($"/v1/staff/patients/{patient.Pseudonym}/homework"), 404);
        await Problem(await noura.PutAsJsonAsync($"/v1/staff/homework/{homeworkId}", new { title = "x", instructions = new[] { "x" }, frequency = "daily", endsOn = today.AddDays(3) }), 404);
        await Problem(await noura.DeleteAsync($"/v1/staff/homework/{homeworkId}"), 404);

        // Cancel: the exercise ends, the patient is told and can no longer check in.
        (await sara.DeleteAsync($"/v1/staff/homework/{homeworkId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        _hosts.Notifier.Sent.Should().Contain(n => n.Kind == "homework.cancelled" && n.RecipientUserId == patient.PatientId);
        list = await Read(await patient.Me.GetAsync("/v1/homework"));
        list.GetProperty("completed").EnumerateArray().Single(h => h.GetProperty("id").GetGuid() == homeworkId)
            .GetProperty("endsOn").GetString().Should().Be(today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), "a cancelled exercise ends the day it was cancelled");
        (await Problem(await patient.Me.PostAsJsonAsync($"/v1/homework/{homeworkId}/checkins", new { status = "done" }), 409)).GetProperty("code").GetString().Should().Be(ErrorCodes.HomeworkEnded);
        (await Read(await sara.GetAsync($"/v1/staff/patients/{patient.Pseudonym}/homework"))).GetProperty("past").EnumerateArray().Single(h => h.GetProperty("id").GetGuid() == homeworkId)
            .GetProperty("status").GetString().Should().Be("cancelled");
    }

    [Fact]
    public async Task The_treatment_plan_needs_the_approved_file_and_is_versioned_with_If_Match()
    {
        var patient = await BookedPatientAsync("أ. سارة م.");
        using var sara = _hosts.ApiClient("ar", await _hosts.StaffSignInAsync("sara@staff.tamaneen.dev"));
        var body = new
        {
            goals = new[] { new { text = "تقليل وقت بدء النوم إلى أقل من 30 دقيقة.", status = "improving" }, new { text = "العودة لنشاط بدني أسبوعي.", status = "not_started" } },
            interventions = "العلاج المعرفي السلوكي للأرق وتمارين التنفس.",
            frequency = "weekly",
            reviewOn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            followUpNotes = "تحسن واضح في النوم.",
        };
        var path = $"/v1/staff/patients/{patient.Pseudonym}/plans";
        var empty = await Get(sara, path);
        empty.Tag.Should().Be("\"0\"");
        empty.Body.GetProperty("current").ValueKind.Should().Be(JsonValueKind.Null);
        empty.Body.GetProperty("fileApproved").GetBoolean().Should().BeFalse();
        (await Problem(await sara.PostAsJsonAsync(path, body), 428)).GetProperty("code").GetString().Should().Be(ErrorCodes.VersionRequired);
        (await Problem(await Send(sara, HttpMethod.Post, path, "\"0\"", body), 409)).GetProperty("code").GetString().Should().Be(ErrorCodes.PlanFileNotApproved);

        await ApproveFileAsync(sara, patient.Pseudonym);
        var first = await Read(await Send(sara, HttpMethod.Post, path, "\"0\"", body));
        first.GetProperty("version").GetInt32().Should().Be(1);
        first.GetProperty("goals")[0].GetProperty("status").GetString().Should().Be("improving");
        (await Problem(await Send(sara, HttpMethod.Post, path, "\"0\"", body), 412)).GetProperty("code").GetString().Should().Be(ErrorCodes.VersionChanged);
        await Problem(await Send(sara, HttpMethod.Post, path, "\"1\"", body with { reviewOn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)) }), 400);
        var second = await Read(await Send(sara, HttpMethod.Post, path, "\"1\"", body with { interventions = "سجل الأفكار والتنشيط السلوكي." }));
        second.GetProperty("version").GetInt32().Should().Be(2);

        var history = await Get(sara, path);
        history.Tag.Should().Be("\"2\"");
        history.Body.GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("version").GetInt32()).Should().Equal(2, 1);
        (await Read(await sara.GetAsync($"{path}/1"))).GetProperty("interventions").GetString().Should().Be("العلاج المعرفي السلوكي للأرق وتمارين التنفس.", "old versions never change");

        // S14 shows the current plan's goals; another therapist sees neither.
        var overview = await Read(await sara.GetAsync($"/v1/staff/patients/{patient.Pseudonym}/overview"));
        overview.GetProperty("plan").GetProperty("version").GetInt32().Should().Be(2);
        overview.GetProperty("fileApproved").GetBoolean().Should().BeTrue();
        using var noura = _hosts.ApiClient("ar", await _hosts.StaffSignInAsync("noura@staff.tamaneen.dev"));
        await Problem(await noura.GetAsync(path), 404);
        await Problem(await noura.GetAsync($"/v1/staff/patients/{patient.Pseudonym}/overview"), 404);
        await using var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Therapist));
        (await db.TreatmentPlanVersions.CountAsync(v => v.PatientId == patient.PatientId)).Should().Be(0, "row-level security hides the plan from a therapist without the assignment");

        var log = await Read(await patient.Me.GetAsync("/v1/me/access-log"));
        log.EnumerateArray().Select(e => e.GetProperty("action").GetString()).Should().Contain(["patient.plan_viewed", "patient.plan_saved", "patient.overview_opened"]);
    }

    [Fact]
    public async Task Session_notes_are_drafted_signed_locked_and_never_visible_to_the_patient()
    {
        var patient = await BookedPatientAsync("أ. سارة م.");
        using var sara = _hosts.ApiClient("ar", await _hosts.StaffSignInAsync("sara@staff.tamaneen.dev"));
        var path = $"/v1/staff/patients/{patient.Pseudonym}/sessions/{patient.AppointmentId}/note";
        var draft = new { template = "soap", fields = new Dictionary<string, string> { ["s"] = "ينام أسرع منذ بدء التمرين.", ["o"] = "هادئ وتواصل جيد.", ["a"] = "تحسن في الأرق.", ["p"] = "بدء التنشيط السلوكي." }, summaryPoints = new[] { "النوم تحسن.", "نجرب نشاطًا صغيرًا هذا الأسبوع." }, shareSummary = true };

        // Not before the session has started.
        (await Problem(await Send(sara, HttpMethod.Put, path, "\"0\"", draft), 422)).GetProperty("code").GetString().Should().Be(ErrorCodes.AppointmentNotStarted);
        await StartAppointmentNowAsync(patient.AppointmentId);

        var view = await Get(sara, path);
        view.Tag.Should().Be("\"0\"");
        view.Body.GetProperty("note").ValueKind.Should().Be(JsonValueKind.Null);
        view.Body.GetProperty("templates").GetProperty("soap").GetArrayLength().Should().Be(4);
        await Problem(await sara.PutAsJsonAsync(path, draft), 428);
        var saved = await Read(await Send(sara, HttpMethod.Put, path, "\"0\"", draft));
        saved.GetProperty("note").GetProperty("status").GetString().Should().Be("draft");
        var revision = saved.GetProperty("note").GetProperty("revision").GetInt32();
        (await Problem(await Send(sara, HttpMethod.Put, path, "\"0\"", draft), 412)).GetProperty("code").GetString().Should().Be(ErrorCodes.VersionChanged);
        saved = await Read(await Send(sara, HttpMethod.Put, path, $"\"{revision}\"", draft with { template = "dap", fields = new Dictionary<string, string> { ["d"] = "تحدثنا عن النوم.", ["a"] = "تحسن.", ["p"] = "متابعة." } }));
        saved.GetProperty("note").GetProperty("fields").GetProperty("d").GetString().Should().Be("تحدثنا عن النوم.");
        revision = saved.GetProperty("note").GetProperty("revision").GetInt32();

        var signed = await Read(await Send(sara, HttpMethod.Post, $"{path}/sign", $"\"{revision}\"", new { }));
        signed.GetProperty("note").GetProperty("status").GetString().Should().Be("signed");
        signed.GetProperty("note").GetProperty("signedBy").GetString().Should().Be("أ. سارة م.");
        revision = signed.GetProperty("note").GetProperty("revision").GetInt32();
        (await Problem(await Send(sara, HttpMethod.Put, path, $"\"{revision}\"", draft), 409)).GetProperty("code").GetString().Should().Be(ErrorCodes.NoteSigned);
        var amended = await Read(await sara.PostAsJsonAsync($"{path}/addenda", new { text = "اتصل المريض بعد الجلسة وأكد الموعد القادم." }));
        amended.GetProperty("note").GetProperty("addenda")[0].GetProperty("text").GetString().Should().Be("اتصل المريض بعد الجلسة وأكد الموعد القادم.");

        // The patient gets the shared points only, never the note.
        var summary = await Read(await patient.Me.GetAsync($"/v1/appointments/{patient.AppointmentId}/session-summary"));
        summary.GetProperty("points").EnumerateArray().Select(p => p.GetString()).Should().Equal("النوم تحسن.", "نجرب نشاطًا صغيرًا هذا الأسبوع.");
        _hosts.Notifier.Sent.Should().Contain(n => n.Kind == "session.summary_shared" && n.RecipientUserId == patient.PatientId);
        await using (var asPatient = _sql.Context(StaticCurrentUser.ForPatient(patient.PatientId)))
        {
            (await asPatient.SessionNotes.CountAsync()).Should().Be(0, "row-level security never shows notes to the patient");
            (await asPatient.SessionNoteAddenda.CountAsync()).Should().Be(0);
            (await asPatient.SessionSummaries.CountAsync()).Should().Be(1);
        }

        using var noura = _hosts.ApiClient("ar", await _hosts.StaffSignInAsync("noura@staff.tamaneen.dev"));
        await Problem(await noura.GetAsync(path), 404);
        var session = (await Read(await sara.GetAsync($"/v1/staff/patients/{patient.Pseudonym}/overview"))).GetProperty("nextAppointment");
        session.GetProperty("id").GetGuid().Should().Be(patient.AppointmentId, "the session under way is still the next one");
        session.GetProperty("noteStatus").GetString().Should().Be("signed");
        var log = await Read(await patient.Me.GetAsync("/v1/me/access-log"));
        log.EnumerateArray().Select(e => e.GetProperty("action").GetString()).Should().Contain(["patient.note_opened", "patient.note_saved", "patient.note_signed"]);
    }

    [Fact]
    public async Task The_safety_plan_is_shared_by_patient_and_therapist_with_one_version_number()
    {
        var patient = await BookedPatientAsync("أ. سارة م.");
        var plan = new { sections = new Dictionary<string, string> { ["warningSigns"] = "أسهر كثيرًا وأنعزل في غرفتي.", ["copingAlone"] = "المشي عشر دقائق." }, contacts = new[] { new { name = "أختي", phone = "0551234567" } } };
        var first = await patient.Me.PutAsJsonAsync("/v1/safety-plan", plan);
        (await Read(first)).GetProperty("version").GetInt32().Should().Be(1);
        first.Headers.ETag!.Tag.Should().Be("\"1\"");
        _hosts.Notifier.Sent.Should().Contain(n => n.Kind == "safety_plan.updated" && n.Data["by"] == "patient");

        using var sara = _hosts.ApiClient("ar", await _hosts.StaffSignInAsync("sara@staff.tamaneen.dev"));
        var path = $"/v1/staff/patients/{patient.Pseudonym}/safety-plan";
        var read = await Get(sara, path);
        read.Tag.Should().Be("\"1\"");
        read.Body.GetProperty("sections").GetProperty("warningSigns").GetString().Should().Be("أسهر كثيرًا وأنعزل في غرفتي.");
        read.Body.GetProperty("contacts")[0].GetProperty("phone").GetString().Should().Be("0551234567");
        read.Body.GetProperty("updatedByRole").GetString().Should().Be("patient");
        read.Body.GetProperty("emergency").GetProperty("emergencyNumber").GetString().Should().Be("911");

        var edit = new { sections = new Dictionary<string, string> { ["warningSigns"] = "أسهر كثيرًا وأنعزل في غرفتي.", ["copingAlone"] = "المشي عشر دقائق والتنفس 4-4-6.", ["reasonToLive"] = "ابنة أختي الصغيرة." }, contacts = new[] { new { name = "أختي", phone = "0551234567" }, new { name = "صديقي خالد", phone = "0559876543" } } };
        await Problem(await sara.PostAsJsonAsync($"/v1/staff/patients/{patient.Pseudonym}/safety-plans", edit), 428);
        (await Problem(await Send(sara, HttpMethod.Post, $"/v1/staff/patients/{patient.Pseudonym}/safety-plans", "\"0\"", edit), 412)).GetProperty("code").GetString().Should().Be(ErrorCodes.VersionChanged);
        var saved = await Read(await Send(sara, HttpMethod.Post, $"/v1/staff/patients/{patient.Pseudonym}/safety-plans", "\"1\"", edit));
        saved.GetProperty("version").GetInt32().Should().Be(2);
        _hosts.Notifier.Sent.Should().Contain(n => n.Kind == "safety_plan.updated" && n.RecipientUserId == patient.PatientId && n.Data["by"] == "therapist");

        // P37: the patient reads version 2; a save based on version 1 is refused, one without If-Match (offline queue) is not.
        var mine = await patient.Me.GetAsync("/v1/safety-plan");
        mine.Headers.ETag!.Tag.Should().Be("\"2\"");
        var mineBody = await Read(mine);
        mineBody.GetProperty("coAuthor").GetString().Should().Be("أ. سارة م.");
        mineBody.GetProperty("contacts").GetArrayLength().Should().Be(2);
        (await Problem(await Send(patient.Me, HttpMethod.Put, "/v1/safety-plan", "\"1\"", plan), 412)).GetProperty("code").GetString().Should().Be(ErrorCodes.VersionChanged);
        (await Read(await Send(patient.Me, HttpMethod.Put, "/v1/safety-plan", "\"2\"", edit))).GetProperty("version").GetInt32().Should().Be(3);

        var versions = await Read(await sara.GetAsync($"{path}/versions"));
        versions.EnumerateArray().Select(v => v.GetProperty("version").GetInt32()).Should().Equal(3, 2, 1);
        versions.EnumerateArray().Select(v => v.GetProperty("authorRole").GetString()).Should().Equal("patient", "therapist", "patient");
        (await Read(await sara.GetAsync($"{path}/versions/1"))).GetProperty("sections").GetProperty("copingAlone").GetString().Should().Be("المشي عشر دقائق.");

        using var noura = _hosts.ApiClient("ar", await _hosts.StaffSignInAsync("noura@staff.tamaneen.dev"));
        await Problem(await noura.GetAsync(path), 404);
        await Problem(await Send(noura, HttpMethod.Post, $"/v1/staff/patients/{patient.Pseudonym}/safety-plans", "\"3\"", edit), 404);
    }

    [Fact]
    public async Task End_of_care_is_blocked_at_high_risk_signed_off_by_a_supervisor_and_then_closes_the_therapists_access()
    {
        var patient = await BookedPatientAsync("أ. سارة م.");
        using var sara = _hosts.ApiClient("ar", await _hosts.StaffSignInAsync("sara@staff.tamaneen.dev"));
        var path = $"/v1/staff/patients/{patient.Pseudonym}/closure";
        var view = await Get(sara, path);
        view.Body.GetProperty("closure").ValueKind.Should().Be(JsonValueKind.Null);
        view.Body.GetProperty("blocked").GetBoolean().Should().BeFalse();
        view.Body.GetProperty("measures").EnumerateArray().Select(m => m.GetProperty("instrument").GetString()).Should().Equal("PHQ-9", "GAD-7");
        var body = new { reasonCode = "goals_met", sessionCount = 8, durationWeeks = 10, patientSummary = "تعلمتِ ملاحظة الأفكار القاسية والرد عليها. هذا جهدك أنتِ.", warningSigns = "قلة النوم، الانعزال", whatToDo = "سجل الأفكار، المشي، أكلم أختي", followUp = "phq9_1m_3m", keepLibrary = true, submit = true };
        var missing = await Problem(await Send(sara, HttpMethod.Put, path, "\"0\"", body with { patientSummary = "" }), 400);
        missing.GetProperty("errors").GetProperty("patientSummary").GetArrayLength().Should().Be(1);
        var submitted = await Read(await Send(sara, HttpMethod.Put, path, "\"0\"", body));
        submitted.GetProperty("closure").GetProperty("status").GetString().Should().Be("submitted");
        _hosts.Notifier.Sent.Should().Contain(n => n.Kind == "care.closure_submitted");

        // The supervisor sends it back once, the therapist resubmits, then the supervisor signs off.
        using var fahad = _hosts.ApiClient("ar", await _hosts.StaffSignInAsync("fahad@staff.tamaneen.dev"));
        var queue = await Read(await fahad.GetAsync("/v1/staff/closures"));
        var id = queue.EnumerateArray().Single(c => c.GetProperty("pseudonymId").GetString() == patient.Pseudonym).GetProperty("id").GetGuid();
        await Problem(await sara.PostAsJsonAsync($"/v1/staff/closures/{id}/sign-off", new { approve = true }), 403);
        await Problem(await fahad.PostAsJsonAsync($"/v1/staff/closures/{id}/sign-off", new { approve = false }), 400);
        (await Read(await fahad.PostAsJsonAsync($"/v1/staff/closures/{id}/sign-off", new { approve = false, reason = "أضيفي خطة المتابعة بوضوح." }))).GetProperty("status").GetString().Should().Be("rejected");
        var rejected = await Get(sara, path);
        rejected.Body.GetProperty("closure").GetProperty("rejectionReason").GetString().Should().Be("أضيفي خطة المتابعة بوضوح.");
        await Read(await Send(sara, HttpMethod.Put, path, rejected.Tag, body));
        var signedOff = await Read(await fahad.PostAsJsonAsync($"/v1/staff/closures/{id}/sign-off", new { approve = true }));
        signedOff.GetProperty("status").GetString().Should().Be("signed_off");
        signedOff.GetProperty("decidedBy").GetString().Should().NotBeNullOrEmpty();
        _hosts.Notifier.Sent.Should().Contain(n => n.Kind == "care.ended" && n.RecipientUserId == patient.PatientId && n.Link == "/care/complete");

        // The assignment ended: the next appointment is cancelled and the therapist can no longer read new data.
        (await Read(await patient.Me.GetAsync($"/v1/appointments/{patient.AppointmentId}"))).GetProperty("status").GetString().Should().Be("cancelled_by_therapist");
        await Read(await patient.Me.PostAsJsonAsync("/v1/mood", new { score = 6, tags = new[] { "calm" }, note = "يوم هادئ" }));
        await Problem(await sara.GetAsync($"/v1/staff/patients/{patient.Pseudonym}/overview"), 404);
        await using (var asTherapist = _sql.Context(StaticCurrentUser.ForStaff(await SaraIdAsync(), AppRoles.Therapist)))
        {
            (await asTherapist.MoodEntries.CountAsync(m => m.PatientId == patient.PatientId)).Should().Be(0);
        }

        // P53.
        var me = await Read(await patient.Me.GetAsync("/v1/me"));
        me.GetProperty("careClosedAt").ValueKind.Should().Be(JsonValueKind.String);
        var closure = await Read(await patient.Me.GetAsync("/v1/me/care-closure"));
        closure.GetProperty("therapistName").GetString().Should().Be("أ. سارة م.");
        closure.GetProperty("sessionCount").GetInt32().Should().Be(8);
        closure.GetProperty("warningSigns").GetString().Should().Be("قلة النوم، الانعزال");
        var file = await patient.Me.GetAsync("/v1/me/care-closure/summary");
        file.IsSuccessStatusCode.Should().BeTrue();
        var text = await file.Content.ReadAsStringAsync();
        text.Should().Contain("هذا جهدك أنتِ").And.NotContain("PHQ");
        // The return asks for the short safety check first (P23 again, through the same gate).
        var noCheck = await Problem(await patient.Me.PostAsync("/v1/me/care/return", null), 409);
        noCheck.GetProperty("code").GetString().Should().Be(ErrorCodes.CareReturnSafetyCheckRequired);
        noCheck.GetProperty("next").GetString().Should().Be("/care/return");
        await Read(await patient.Me.PostAsJsonAsync("/v1/screening/cssrs", new { answers = new[] { false, false, false, false, false, false } }));
        (await Read(await patient.Me.PostAsync("/v1/me/care/return", null))).GetProperty("next").GetString().Should().Be("/matches");
        await using (var lead = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor)))
        {
            (await lead.AuditEntries.Where(a => a.Action == "care.return_requested" && a.PatientPseudonymId == patient.Pseudonym).Select(a => a.Reason).SingleAsync()).Should().StartWith("screening=");
        }
        var matches = await Read(await patient.Me.GetAsync("/v1/matches"));
        matches.GetProperty("items")[0].GetProperty("therapist").GetProperty("displayName").GetString().Should().Be("أ. سارة م.", "the previous therapist comes first");
    }

    [Fact]
    public async Task Closing_is_refused_while_the_risk_is_high()
    {
        var patient = await BookedPatientAsync("أ. سارة م.");
        using var sara = _hosts.ApiClient("ar", await _hosts.StaffSignInAsync("sara@staff.tamaneen.dev"));
        await Read(await sara.PostAsJsonAsync($"/v1/staff/patients/{patient.Pseudonym}/risk-flag", new { riskLevel = 3, reason = "ذكر أفكارًا مقلقة" }));
        var path = $"/v1/staff/patients/{patient.Pseudonym}/closure";
        (await Read(await sara.GetAsync(path))).GetProperty("blocked").GetBoolean().Should().BeTrue();
        var body = new { reasonCode = "goals_met", patientSummary = "ملخص.", warningSigns = "قلة النوم", whatToDo = "المشي", submit = true };
        (await Problem(await Send(sara, HttpMethod.Put, path, "\"0\"", body), 409)).GetProperty("code").GetString().Should().Be(ErrorCodes.ClosureBlocked);
    }

    [Fact]
    public async Task Changing_therapist_goes_through_the_supervisor_and_the_new_booking_ends_the_old_assignment()
    {
        var patient = await BookedPatientAsync("أ. سارة م.");
        var reasons = await Read(await patient.Me.GetAsync("/v1/catalogs/change-reasons"));
        reasons.EnumerateArray().Select(r => r.GetProperty("label").GetString()).Should().Contain("لم أشعر بالارتياح");
        await Problem(await patient.Me.GetAsync("/v1/me/change-therapist-requests/current"), 404);
        await Problem(await patient.Me.PostAsJsonAsync("/v1/me/change-therapist-requests", new { reasonCode = "unknown" }), 400);
        var created = await Read(await patient.Me.PostAsJsonAsync("/v1/me/change-therapist-requests", new { reasonCode = "not_comfortable" }));
        created.GetProperty("status").GetString().Should().Be("requested");
        created.GetProperty("withinHours").GetInt32().Should().Be(24);
        var again = await Problem(await patient.Me.PostAsJsonAsync("/v1/me/change-therapist-requests", new { }), 409);
        again.GetProperty("code").GetString().Should().Be(ErrorCodes.ChangeRequestOpen);
        again.GetProperty("current").GetProperty("id").GetGuid().Should().Be(created.GetProperty("id").GetGuid());
        _hosts.Notifier.Sent.Should().Contain(n => n.Kind == "care.change_requested");
        (await Read(await patient.Me.GetAsync("/v1/me"))).GetProperty("onboarding").GetProperty("triageStatus").GetString().Should().NotBe("pending", "a change request never sends the patient back to the waiting screen");

        // The current therapist never sees the request or its reason.
        var saraId = await SaraIdAsync();
        await using (var asSara = _sql.Context(StaticCurrentUser.ForStaff(saraId, AppRoles.Therapist)))
        {
            (await asSara.ChangeTherapistRequests.CountAsync()).Should().Be(0);
        }

        // S03: the case is in the queue with path "change"; Sara cannot be offered again.
        using var fahad = _hosts.ApiClient("ar", await _hosts.StaffSignInAsync("fahad@staff.tamaneen.dev"));
        var queue = await Read(await fahad.GetAsync("/v1/staff/triage"));
        var item = queue.EnumerateArray().Single(q => q.GetProperty("pseudonymId").GetString() == patient.Pseudonym);
        item.GetProperty("path").GetString().Should().Be("change");
        var caseId = item.GetProperty("id").GetGuid();
        await Read(await fahad.PostAsync($"/v1/staff/triage/{caseId}/claim", null));
        (await Read(await patient.Me.GetAsync("/v1/me/change-therapist-requests/current"))).GetProperty("status").GetString().Should().Be("matching");
        var therapists = await Read(await fahad.GetAsync($"/v1/staff/triage/{caseId}/therapists"));
        therapists.EnumerateArray().Should().NotContain(t => t.GetProperty("id").GetGuid() == saraId);
        await Problem(await fahad.PostAsJsonAsync($"/v1/staff/triage/{caseId}/decisions", new { kind = "approve", therapistIds = new[] { saraId } }), 409);
        var other = therapists.EnumerateArray().First().GetProperty("id").GetGuid();
        await Read(await fahad.PostAsJsonAsync($"/v1/staff/triage/{caseId}/decisions", new { kind = "approve", therapistIds = new[] { other } }));
        (await Read(await patient.Me.GetAsync("/v1/me/change-therapist-requests/current"))).GetProperty("status").GetString().Should().Be("offered");
        (await Read(await patient.Me.GetAsync("/v1/matches"))).GetProperty("items").EnumerateArray().Select(m => m.GetProperty("therapist").GetProperty("id").GetGuid()).Should().Equal(other);

        // The patient books the new therapist: the old assignment ends and the old therapist is told, without the reason.
        await BookAsync(patient.Me, other);
        await Problem(await patient.Me.GetAsync("/v1/me/change-therapist-requests/current"), 404);
        using var sara = _hosts.ApiClient("ar", await _hosts.StaffSignInAsync("sara@staff.tamaneen.dev"));
        await Problem(await sara.GetAsync($"/v1/staff/patients/{patient.Pseudonym}/overview"), 404);
        var transferred = _hosts.Notifier.Sent.Single(n => n.Kind == "care.transferred" && n.RecipientUserId == saraId && n.Data["pseudonymId"] == patient.Pseudonym);
        transferred.Data.Values.Should().NotContain("not_comfortable");
        await using var asSupervisor = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor));
        (await asSupervisor.CareAssignments.Where(a => a.PatientId == patient.PatientId && a.EndedAtUtc == null).Select(a => a.TherapistUserId).ToListAsync()).Should().Equal(other);
    }

    // ---------- helpers ----------

    private sealed record Patient(HttpClient Me, string Pseudonym, Guid PatientId, Guid AppointmentId);

    private sealed record Versioned(JsonElement Body, string Tag);

    private static async Task<Versioned> Get(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var body = await Read(response);
        return new Versioned(body, response.Headers.ETag!.Tag);
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string etag, object body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.IfMatch.Add(EntityTagHeaderValue.Parse(etag));
        return client.SendAsync(request);
    }

    private static async Task ApproveFileAsync(HttpClient therapist, string pseudonym)
    {
        var file = await Read(await therapist.GetAsync($"/v1/staff/patients/{pseudonym}/file"));
        await Read(await Send(therapist, HttpMethod.Post, $"/v1/staff/patients/{pseudonym}/file/approve", $"\"{file.GetProperty("version").GetString()}\"", new { }));
    }

    private async Task<Guid> SaraIdAsync()
    {
        await using var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor));
        return await db.StaffProfiles.Where(s => s.DisplayNameEn == "Sara M.").Select(s => s.UserId).SingleAsync();
    }

    /// <summary>Moves the appointment's start to a minute ago, so the session note can be written.</summary>
    private async Task StartAppointmentNowAsync(Guid appointmentId)
    {
        await using var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor));
        var start = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [clinical].[Appointments] SET [StartsAtUtc] = {start} WHERE [Id] = {appointmentId}");
    }

    private static async Task BookAsync(HttpClient me, Guid therapistId)
    {
        var slots = await Read(await me.GetAsync($"/v1/therapists/{therapistId}/slots?days=14"));
        var hold = await Read(await me.PostAsJsonAsync("/v1/bookings/holds", new { slotId = slots[0].GetProperty("slotId").GetGuid() }));
        await Read(await me.PostAsync($"/v1/bookings/holds/{hold.GetProperty("holdId").GetGuid()}/confirm", null));
    }

    /// <summary>A new Arabic patient on the direct path, booked with the named therapist (which creates the assignment).</summary>
    private async Task<Patient> BookedPatientAsync(string therapistName)
    {
        using var anon = _hosts.ApiClient("ar");
        var email = $"care-{Guid.NewGuid():N}@example.com";
        var eligibility = await Read(await anon.PostAsJsonAsync("/v1/auth/eligibility", new { isAdult = true, cityCode = "RUH" }));
        var challenge = await Read(await anon.PostAsJsonAsync("/v1/auth/signup", new { eligibilityToken = eligibility.GetProperty("eligibilityToken").GetString(), nickname = "نخلة هادئة", contactType = "email", contact = email, password = Password, language = "ar" }));
        var verified = await Read(await anon.PostAsJsonAsync("/v1/auth/signup/verify", new { challengeId = challenge.GetProperty("challengeId").GetGuid(), code = _hosts.CodeSentTo(email) }));
        var me = _hosts.ApiClient("ar", verified.GetProperty("tokens").GetProperty("accessToken").GetString());
        await Read(await me.PutAsJsonAsync("/v1/me/consents", new { items = new[] { "platform", "ai", "share_with_therapist" }.Select(k => new { key = k, granted = true }) }));
        await Read(await PutSafetyInfoAsync(me, new { shareLocation = false }));
        await Read(await me.PutAsJsonAsync("/v1/me/path", new { path = "direct" }));
        await Read(await me.PostAsJsonAsync("/v1/screening/cssrs", new { answers = new[] { false, false, false, false, false, false } }));
        var directory = await Read(await me.GetAsync("/v1/therapists"));
        var therapistId = directory.EnumerateArray().First(t => t.GetProperty("displayName").GetString() == therapistName).GetProperty("id").GetGuid();
        await BookAsync(me, therapistId);
        var appointments = await Read(await me.GetAsync("/v1/appointments"));
        var appointmentId = appointments.GetProperty("upcoming")[0].GetProperty("id").GetGuid();
        var pseudonym = (await Read(await me.GetAsync("/v1/me"))).GetProperty("pseudonymId").GetString()!;
        await using var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor));
        var patientId = await db.UserAccounts.Where(a => a.PseudonymId == pseudonym).Select(a => a.Id).SingleAsync();
        return new Patient(me, pseudonym, patientId, appointmentId);
    }
}
