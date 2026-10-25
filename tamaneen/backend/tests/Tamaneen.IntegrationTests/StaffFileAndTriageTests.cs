using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using static Tamaneen.IntegrationTests.JourneyHosts;

namespace Tamaneen.IntegrationTests;

/// <summary>
/// S02 (clinical file and evidence inspector) and S03 (triage review) through the real API, crisis worker and SQL
/// Server row-level security: evidence links, edits and dismissals, the If-Match approval, risk raising, the manual
/// crisis, claims, decisions with reasons, the clinical lead's co-sign and the crisis floor.
/// </summary>
[Collection(SqlServerGroup.Name)]
public sealed class StaffFileAndTriageTests : IAsyncLifetime
{
    private readonly SqlServerFixture _sql;
    private JourneyHosts _hosts = null!;

    public StaffFileAndTriageTests(SqlServerFixture sql) => _sql = sql;

    public Task InitializeAsync()
    {
        _hosts = new JourneyHosts(_sql);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _hosts.DisposeAsync();

    [Fact]
    public async Task The_treating_therapist_checks_the_evidence_edits_dismisses_and_approves_the_file()
    {
        var (me, pseudonym, caseId) = await SubmittedIntakeAsync();
        using var supervisor = _hosts.ApiClient("ar", await _hosts.StaffSignInAsync("fahad@staff.tamaneen.dev"));
        await Read(await supervisor.PostAsync($"/v1/staff/triage/{caseId}/claim", null));
        var sara = await SaraIdAsync(supervisor, caseId);
        await Read(await supervisor.PostAsJsonAsync($"/v1/staff/triage/{caseId}/decisions", new { kind = "approve", therapistIds = new[] { sara }, focus = new[] { "stress", "sleep" } }));
        await BookAsync(me, sara);

        var saraToken = await _hosts.StaffSignInAsync("sara@staff.tamaneen.dev");
        using var therapist = _hosts.ApiClient("ar", saraToken);
        var response = await therapist.GetAsync($"/v1/staff/patients/{pseudonym}/file");
        var file = await Read(response);
        response.Headers.ETag!.Tag.Trim('"').Should().Be(file.GetProperty("version").GetString());
        file.GetProperty("generatedByAi").GetBoolean().Should().BeTrue();
        file.GetProperty("canEdit").GetBoolean().Should().BeTrue();
        var statements = file.GetProperty("statements").EnumerateArray().ToDictionary(s => s.GetProperty("key").GetString()!);
        statements["concern"].GetProperty("text").GetString().Should().Be("تشعر بضغط كبير في العمل منذ ثلاثة أشهر، ويصعب عليك النوم.", "the citation is stripped");
        statements["concern"].GetProperty("sourceCount").GetInt32().Should().Be(2);
        statements["goals"].GetProperty("editedByPatient").GetBoolean().Should().BeTrue();
        statements["hypotheses"].GetProperty("hypothesis").GetBoolean().Should().BeTrue();
        statements.Should().NotContainKey("priorCare", "a statement with no source message is not shown (EC-15)");
        file.GetProperty("transcript").EnumerateArray().Select(t => t.GetProperty("turn").GetInt32()).Should().StartWith([1, 2, 3]);
        var phq9 = file.GetProperty("phq9");
        phq9.GetProperty("total").GetInt32().Should().Be(8);
        phq9.GetProperty("severity").GetString().Should().Be("mild");
        phq9.GetProperty("item9").GetInt32().Should().Be(0);
        phq9.GetProperty("items").GetArrayLength().Should().Be(9);
        var correction = file.GetProperty("corrections").EnumerateArray().Single(c => c.GetProperty("key").GetString() == "goals");
        correction.GetProperty("aiText").GetString().Should().Be("أن تتعلم طرقًا لتهدئة نفسك قبل النوم.");
        correction.GetProperty("patientText").GetString().Should().Be("أن أنام أفضل وأعود للرياضة.");

        // SB-12: the sources of the first paragraph are the patient's turns 2 and 4.
        var evidence = await Read(await therapist.GetAsync($"/v1/staff/patients/{pseudonym}/evidence/concern"));
        evidence.GetProperty("turns").EnumerateArray().Select(t => t.GetInt32()).Should().Equal(2, 4);
        evidence.GetProperty("removed").GetBoolean().Should().BeFalse();

        // SB-14: dismissing needs a reason and hides the statement.
        (await Problem(await therapist.PostAsJsonAsync($"/v1/staff/patients/{pseudonym}/file/dismiss-item", new { statementKey = "hypotheses" }), 400))
            .GetProperty("code").GetString().Should().Be("error.reason.required");
        file = await Read(await therapist.PostAsJsonAsync($"/v1/staff/patients/{pseudonym}/file/dismiss-item", new { statementKey = "hypotheses", reason = "مبكر جدًا" }));
        file.GetProperty("statements").EnumerateArray().Should().NotContain(s => s.GetProperty("key").GetString() == "hypotheses");

        // SB-15: approval needs the version just read; an old one gets 412.
        var edits = new { edits = new Dictionary<string, string> { ["concern"] = "ضغط عمل منذ ثلاثة أشهر مع أرق أولي." } };
        (await Problem(await therapist.PostAsJsonAsync($"/v1/staff/patients/{pseudonym}/file/approve", edits), 428)).GetProperty("code").GetString().Should().Be("error.file.version_required");
        (await Problem(await Approve(therapist, pseudonym, response.Headers.ETag.Tag, edits), 412)).GetProperty("code").GetString().Should().Be("error.file.version_changed");
        file = await Read(await Approve(therapist, pseudonym, $"\"{file.GetProperty("version").GetString()}\"", edits));
        file.GetProperty("approval").GetProperty("approved").GetBoolean().Should().BeTrue();
        file.GetProperty("approval").GetProperty("approvedBy").GetString().Should().Be("أ. سارة م.");
        file.GetProperty("canEdit").GetBoolean().Should().BeFalse();
        var concern = file.GetProperty("statements").EnumerateArray().Single(s => s.GetProperty("key").GetString() == "concern");
        concern.GetProperty("editedByTherapist").GetBoolean().Should().BeTrue();
        concern.GetProperty("text").GetString().Should().Be("ضغط عمل منذ ثلاثة أشهر مع أرق أولي.");
        (await Problem(await therapist.PostAsJsonAsync($"/v1/staff/patients/{pseudonym}/file/dismiss-item", new { statementKey = "goals", reason = "x" }), 409)).GetProperty("code").GetString().Should().Be("error.file.approved");

        // SB-16: usefulness rating.
        (await therapist.PostAsJsonAsync($"/v1/staff/patients/{pseudonym}/file-feedback", new { rating = 4, comment = "مفيد" })).StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
        await Problem(await therapist.PostAsJsonAsync($"/v1/staff/patients/{pseudonym}/file-feedback", new { rating = 6 }), 400);
        (await Read(await therapist.GetAsync($"/v1/staff/patients/{pseudonym}/file"))).GetProperty("feedback").GetProperty("rating").GetInt32().Should().Be(4);

        // SB-17: only a higher level, always with a reason.
        (await Problem(await therapist.PostAsJsonAsync($"/v1/staff/patients/{pseudonym}/risk-flag", new { riskLevel = 1, reason = "x" }), 400)).GetProperty("code").GetString().Should().Be("error.risk.not_higher");
        await Problem(await therapist.PostAsJsonAsync($"/v1/staff/patients/{pseudonym}/risk-flag", new { riskLevel = 2 }), 400);
        (await Read(await therapist.PostAsJsonAsync($"/v1/staff/patients/{pseudonym}/risk-flag", new { riskLevel = 2, reason = "تدهور النوم" }))).GetProperty("riskLevel").GetInt32().Should().Be(2);

        // Another therapist cannot read the file or its evidence (row-level security).
        using var noura = _hosts.ApiClient("ar", await _hosts.StaffSignInAsync("noura@staff.tamaneen.dev"));
        await Problem(await noura.GetAsync($"/v1/staff/patients/{pseudonym}/file"), 404);
        await Problem(await noura.GetAsync($"/v1/staff/patients/{pseudonym}/evidence/concern"), 404);
        await Problem(await noura.PostAsJsonAsync($"/v1/staff/patients/{pseudonym}/crisis", new { level = 3, note = "x" }), 404);
        (await noura.GetFromJsonAsync<JsonElement>("/v1/staff/patients")).EnumerateArray().Should().NotContain(p => p.GetProperty("pseudonymId").GetString() == pseudonym);
        (await therapist.GetFromJsonAsync<JsonElement>("/v1/staff/patients")).EnumerateArray().Should().Contain(p => p.GetProperty("pseudonymId").GetString() == pseudonym);

        // SB-18: a manual crisis opens an event through the worker, locks the chat, and carries the note.
        var crisis = await Read(await therapist.PostAsJsonAsync($"/v1/staff/patients/{pseudonym}/crisis", new { level = 3, note = "ذكر أفكار إيذاء في الجلسة" }));
        var eventId = crisis.GetProperty("crisisEventId").GetGuid();
        (await Read(await me.GetAsync("/v1/me"))).GetProperty("chatLocked").GetBoolean().Should().BeTrue();
        using var console = _hosts.WorkerClient("ar", saraToken);
        var detail = await Read(await console.GetAsync($"/v1/crisis/events/{eventId}"));
        detail.GetProperty("source").GetString().Should().Be("staff");
        detail.GetProperty("timeline").EnumerateArray().Should().Contain(t => t.GetProperty("text").GetString()!.Contains("أفكار إيذاء"));
        file = await Read(await therapist.GetAsync($"/v1/staff/patients/{pseudonym}/file"));
        file.GetProperty("riskLevel").GetInt32().Should().Be(3);
        file.GetProperty("crisisFloor").GetInt32().Should().Be(3);

        var log = await Read(await me.GetAsync("/v1/me/access-log"));
        log.EnumerateArray().Select(e => e.GetProperty("action").GetString()).Should().Contain(["patient.file_opened", "patient.evidence_viewed", "patient.file_approved", "crisis.manual_raised", "triage.reviewed"]);
    }

    [Fact]
    public async Task Triage_decisions_need_a_claim_a_reason_and_the_leads_co_sign_to_lower_high_risk()
    {
        var (me, pseudonym, caseId) = await SubmittedIntakeAsync();
        using var supervisor = _hosts.ApiClient("en", await _hosts.StaffSignInAsync("fahad@staff.tamaneen.dev"));
        using var lead = _hosts.ApiClient("en", await _hosts.StaffSignInAsync("lead@staff.tamaneen.dev"));

        var queue = await Read(await supervisor.GetAsync("/v1/staff/triage"));
        var item = queue.EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == caseId);
        item.GetProperty("pseudonymId").GetString().Should().Be(pseudonym);
        (item.GetProperty("dueAt").GetDateTimeOffset() - item.GetProperty("submittedAt").GetDateTimeOffset()).Should().Be(TimeSpan.FromHours(24), "level 1 is reviewed within 24 hours");

        var detail = await Read(await supervisor.GetAsync($"/v1/staff/triage/{caseId}"));
        detail.GetProperty("ruleReasons").EnumerateArray().Select(r => r.GetString()).Should().Equal("routine");
        detail.GetProperty("summary").GetString().Should().StartWith("تشعر بضغط كبير", "the patient's own summary, in the patient's language");
        detail.GetProperty("suggestions").GetArrayLength().Should().BeInRange(1, 3);
        detail.GetProperty("suggestions")[0].GetProperty("capacity").GetInt32().Should().BePositive();

        // No decision without a claim; a claim held by someone else names them.
        (await Problem(await supervisor.PostAsJsonAsync($"/v1/staff/triage/{caseId}/decisions", new { kind = "approve" }), 409)).GetProperty("code").GetString().Should().Be("error.triage.claim_required");
        var claimed = await Read(await supervisor.PostAsync($"/v1/staff/triage/{caseId}/claim", null));
        claimed.GetProperty("claim").GetProperty("mine").GetBoolean().Should().BeTrue();
        (claimed.GetProperty("claim").GetProperty("until").GetDateTimeOffset() - claimed.GetProperty("serverNow").GetDateTimeOffset()).Should().BeCloseTo(TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(5));
        var taken = await Problem(await lead.PostAsync($"/v1/staff/triage/{caseId}/claim", null), 409);
        taken.GetProperty("code").GetString().Should().Be("error.triage.claimed");
        taken.GetProperty("claimedBy").GetString().Should().Be("Dr. Fahad R.");

        // Raise to 3 (reason required), then lowering from 3 waits for the clinical lead.
        await Problem(await supervisor.PostAsJsonAsync($"/v1/staff/triage/{caseId}/decisions", new { kind = "override_risk", riskLevel = 3 }), 400);
        var raised = await Read(await supervisor.PostAsJsonAsync($"/v1/staff/triage/{caseId}/decisions", new { kind = "override_risk", riskLevel = 3, reason = "Item answers suggest more risk" }));
        raised.GetProperty("riskLevel").GetInt32().Should().Be(3);
        var lowered = await Read(await supervisor.PostAsJsonAsync($"/v1/staff/triage/{caseId}/decisions", new { kind = "override_risk", riskLevel = 1, reason = "Spoke with the patient; no risk" }));
        lowered.GetProperty("coSignRequested").GetBoolean().Should().BeTrue();
        lowered.GetProperty("riskLevel").GetInt32().Should().Be(3);
        (await Problem(await supervisor.PostAsJsonAsync($"/v1/staff/triage/{caseId}/decisions", new { kind = "approve" }), 409)).GetProperty("code").GetString().Should().Be("error.triage.cosign_pending");
        (await Problem(await supervisor.PostAsJsonAsync($"/v1/staff/triage/{caseId}/decisions", new { kind = "cosign", approve = true, reason = "self" }), 403)).GetProperty("code").GetString().Should().Be("error.triage.cosign_not_allowed");
        var leadView = await Read(await lead.GetAsync($"/v1/staff/triage/{caseId}"));
        leadView.GetProperty("canCoSign").GetBoolean().Should().BeTrue();
        leadView.GetProperty("pendingCoSign").GetProperty("riskLevel").GetInt32().Should().Be(1);
        (await Read(await lead.PostAsJsonAsync($"/v1/staff/triage/{caseId}/decisions", new { kind = "cosign", approve = true, reason = "Agree after review" }))).GetProperty("riskLevel").GetInt32().Should().Be(1);

        // The older review endpoint can no longer lower a level either.
        await Read(await supervisor.PostAsJsonAsync($"/v1/staff/triage/{caseId}/decisions", new { kind = "override_risk", riskLevel = 2, reason = "Elevated scores" }));
        (await Problem(await supervisor.PostAsJsonAsync($"/v1/staff/triage/{caseId}/review", new { riskLevel = 1, focus = new[] { "stress" } }), 409)).GetProperty("code").GetString().Should().Be("error.triage.lowering_needs_decision");

        // Change the suggestions (reason required), then approve: the patient sees the chosen therapist.
        var sara = await SaraIdAsync(supervisor, caseId);
        await Problem(await supervisor.PostAsJsonAsync($"/v1/staff/triage/{caseId}/decisions", new { kind = "reassign", therapistIds = new[] { sara } }), 400);
        await Read(await supervisor.PostAsJsonAsync($"/v1/staff/triage/{caseId}/decisions", new { kind = "reassign", therapistIds = new[] { sara }, focus = new[] { "stress" }, reason = "Evening sessions" }));
        var approved = await Read(await supervisor.PostAsJsonAsync($"/v1/staff/triage/{caseId}/decisions", new { kind = "approve" }));
        approved.GetProperty("status").GetString().Should().Be("reviewed");
        var matches = await Read(await me.GetAsync("/v1/matches"));
        matches.GetProperty("status").GetString().Should().Be("ready");
        matches.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("therapist").GetProperty("id").GetGuid()).Should().Equal(sara);
        (await Read(await supervisor.GetAsync("/v1/staff/triage"))).EnumerateArray().Should().NotContain(t => t.GetProperty("id").GetGuid() == caseId);
    }

    [Fact]
    public async Task An_open_crisis_sets_a_floor_escalation_opens_an_event_and_closing_shows_referrals()
    {
        var (_, pseudonym, caseId) = await SubmittedIntakeAsync();
        using var supervisor = _hosts.ApiClient("en", await _hosts.StaffSignInAsync("fahad@staff.tamaneen.dev"));
        using var lead = _hosts.ApiClient("en", await _hosts.StaffSignInAsync("lead@staff.tamaneen.dev"));
        var crisis = await Read(await supervisor.PostAsJsonAsync($"/v1/staff/patients/{pseudonym}/crisis", new { level = 4, note = "Called the line in distress" }));
        await Read(await lead.PostAsync($"/v1/staff/triage/{caseId}/claim", null));
        var below = await Problem(await lead.PostAsJsonAsync($"/v1/staff/triage/{caseId}/decisions", new { kind = "override_risk", riskLevel = 2, reason = "Calmer now" }), 422);
        below.GetProperty("code").GetString().Should().Be("error.triage.below_crisis_floor");
        var escalated = await Read(await lead.PostAsJsonAsync($"/v1/staff/triage/{caseId}/decisions", new { kind = "escalate", reason = "Needs the crisis team" }));
        escalated.GetProperty("status").GetString().Should().Be("escalated");
        escalated.GetProperty("crisisEventId").GetGuid().Should().Be(crisis.GetProperty("crisisEventId").GetGuid(), "the open event is raised, not duplicated");
        escalated.GetProperty("riskLevel").GetInt32().Should().Be(4);

        var (other, _, otherCase) = await SubmittedIntakeAsync();
        await Read(await supervisor.PostAsync($"/v1/staff/triage/{otherCase}/claim", null));
        await Problem(await supervisor.PostAsJsonAsync($"/v1/staff/triage/{otherCase}/decisions", new { kind = "close", referrals = new[] { "primary_care" } }), 400);
        await Read(await supervisor.PostAsJsonAsync($"/v1/staff/triage/{otherCase}/decisions", new { kind = "close", referrals = new[] { "primary_care", "community_support" }, reason = "Better served by primary care" }));
        var state = await Read(await other.GetAsync("/v1/triage"));
        state.GetProperty("status").GetString().Should().Be("closed");
        state.GetProperty("referrals").EnumerateArray().Select(r => r.GetString()).Should().Equal("primary_care", "community_support");
        (await Problem(await other.PostAsJsonAsync("/v1/bookings/holds", new { slotId = Guid.NewGuid() }), 403)).GetProperty("code").GetString().Should().Be("error.booking.not_eligible");
    }

    private static Task<HttpResponseMessage> Approve(HttpClient client, string pseudonym, string etag, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/staff/patients/{pseudonym}/file/approve") { Content = JsonContent.Create(body) };
        request.Headers.IfMatch.Add(EntityTagHeaderValue.Parse(etag));
        return client.SendAsync(request);
    }

    private static async Task<Guid> SaraIdAsync(HttpClient supervisor, Guid caseId)
    {
        var therapists = await Read(await supervisor.GetAsync($"/v1/staff/triage/{caseId}/therapists"));
        return therapists.EnumerateArray().First(t => t.GetProperty("displayName").GetString() is "أ. سارة م." or "Sara M.").GetProperty("id").GetGuid();
    }

    private static async Task BookAsync(HttpClient me, Guid therapistId)
    {
        var slots = await Read(await me.GetAsync($"/v1/therapists/{therapistId}/slots?days=14"));
        var hold = await Read(await me.PostAsJsonAsync("/v1/bookings/holds", new { slotId = slots[0].GetProperty("slotId").GetGuid() }));
        await Read(await me.PostAsync($"/v1/bookings/holds/{hold.GetProperty("holdId").GetGuid()}/confirm", null));
    }

    /// <summary>A new Arabic or English patient through intake (two turns, PHQ-9 = 8, GAD-7 = 7), one corrected section, submitted.</summary>
    private async Task<(HttpClient Me, string Pseudonym, Guid CaseId)> SubmittedIntakeAsync()
    {
        var email = $"file-{Guid.NewGuid():N}@example.com";
        using var anon = _hosts.ApiClient("ar");
        var eligibility = await Read(await anon.PostAsJsonAsync("/v1/auth/eligibility", new { isAdult = true, cityCode = "RUH" }));
        var challenge = await Read(await anon.PostAsJsonAsync("/v1/auth/signup", new { eligibilityToken = eligibility.GetProperty("eligibilityToken").GetString(), nickname = "نخلة هادئة", contactType = "email", contact = email, password = "Quiet-Morning-Walk-77", language = "ar" }));
        var verified = await Read(await anon.PostAsJsonAsync("/v1/auth/signup/verify", new { challengeId = challenge.GetProperty("challengeId").GetGuid(), code = _hosts.CodeSentTo(email) }));
        var me = _hosts.ApiClient("ar", verified.GetProperty("tokens").GetProperty("accessToken").GetString());
        await Read(await me.PutAsJsonAsync("/v1/me/consents", new { items = new[] { "platform", "ai", "share_with_therapist" }.Select(k => new { key = k, granted = true }) }));
        await Read(await PutSafetyInfoAsync(me, new { shareLocation = false }));
        await Read(await me.PutAsJsonAsync("/v1/me/path", new { path = "intake" }));
        var intake = await Read(await me.PostAsync("/v1/intake", null));
        var intakeId = intake.GetProperty("intakeId").GetGuid();
        await Read(await me.PostAsJsonAsync($"/v1/intake/{intakeId}/messages", new { text = "أشعر بضغط كبير في الشغل وما أنام زين" }));
        await Read(await me.PostAsJsonAsync($"/v1/intake/{intakeId}/messages", new { text = "الضغط في العمل من ثلاثة أشهر ويصعب علي النوم" }));
        foreach (var (instrument, items) in new[] { ("phq9", 9), ("gad7", 7) })
        {
            for (var item = 1; item <= items; item++)
            {
                await Read(await me.PostAsJsonAsync($"/v1/intake/{intakeId}/answers", new { instrument, item, value = (int?)(instrument == "phq9" && item == 9 ? 0 : 1) }));
            }
        }

        await Read(await me.PostAsJsonAsync($"/v1/intake/{intakeId}/messages", new { text = "أتمنى أتعلم كيف أهدّي نفسي قبل النوم" }));
        await Read(await me.PostAsync($"/v1/intake/{intakeId}/complete", null));
        await Read(await me.PutAsJsonAsync($"/v1/intake/{intakeId}/summary", new { sections = new Dictionary<string, string> { ["goals"] = "أن أنام أفضل وأعود للرياضة." } }));
        await Read(await me.PostAsync($"/v1/intake/{intakeId}/submit", null));
        var pseudonym = (await Read(await me.GetAsync("/v1/me"))).GetProperty("pseudonymId").GetString()!;
        await using var db = _sql.Context(Tamaneen.SharedKernel.StaticCurrentUser.ForStaff(Guid.NewGuid(), Tamaneen.SharedKernel.AppRoles.Supervisor));
        var patientId = db.UserAccounts.Single(a => a.PseudonymId == pseudonym).Id;
        var caseId = db.TriageCases.Where(t => t.PatientId == patientId).OrderByDescending(t => t.SubmittedAtUtc).First().Id;
        return (me, pseudonym, caseId);
    }
}
