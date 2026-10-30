using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tamaneen.Api.Auth;
using Tamaneen.Api.Notifications;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Crisis;
using Tamaneen.Identity;
using Tamaneen.Identity.Auth;
using Tamaneen.Infrastructure.Auth;
using Tamaneen.SharedKernel;
using Tamaneen.SharedKernel.Notifications;
using static Tamaneen.IntegrationTests.JourneyHosts;

namespace Tamaneen.IntegrationTests;

/// <summary>
/// S20 alerts, S21 professional profile, S32 account settings and S41 staff inbox, for a therapist created for
/// the test (so moving the authenticator or changing the profile leaves the seeded staff alone).
/// </summary>
[Collection(SqlServerGroup.Name)]
public sealed class StaffAccountTests : IAsyncLifetime
{
    private const string Password = "Steady-Harbour-Lamp-42";
    private readonly SqlServerFixture _sql;
    private JourneyHosts _hosts = null!;

    public StaffAccountTests(SqlServerFixture sql) => _sql = sql;

    public Task InitializeAsync()
    {
        _hosts = new JourneyHosts(_sql);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _hosts.DisposeAsync();

    [Fact]
    public async Task The_profile_saves_both_languages_refuses_contact_details_and_capacity_below_the_caseload()
    {
        var t = await TherapistAsync();
        using var staff = _hosts.ApiClient("ar", await SignInAsync(t));
        var profile = await Read(await staff.GetAsync("/v1/staff/profile"));
        profile.GetProperty("currentPatients").GetInt32().Should().Be(0);
        profile.GetProperty("specialtyCatalog").GetArrayLength().Should().Be(TherapistProfile.SpecialtyCodes.Count);

        var p1 = await _sql.CreatePatientAsync("أ");
        var p2 = await _sql.CreatePatientAsync("ب");
        await _sql.AssignAsync(p1.Id, t.Id);
        await _sql.AssignAsync(p2.Id, t.Id);

        var problem = await Problem(await staff.PutAsJsonAsync("/v1/staff/profile", Body(capacity: 1)), 400);
        problem.GetProperty("code").GetString().Should().Be(ErrorCodes.CapacityBelowCaseload);
        problem.GetProperty("detail").GetString().Should().Contain("2");

        problem = await Problem(await staff.PutAsJsonAsync("/v1/staff/profile", Body(bioEn: "Call me on 0551234567 or see www.example.com")), 400);
        problem.GetProperty("code").GetString().Should().Be(ErrorCodes.ProfileContactDetails);
        problem.GetProperty("errors").TryGetProperty("bioEn", out _).Should().BeTrue();

        (await Problem(await staff.PutAsJsonAsync("/v1/staff/profile", Body(displayNameAr: "")), 400)).GetProperty("errors").TryGetProperty("displayNameAr", out _).Should().BeTrue();

        var saved = await Read(await staff.PutAsJsonAsync("/v1/staff/profile", Body(capacity: 12)));
        saved.GetProperty("capacity").GetInt32().Should().Be(12);
        saved.GetProperty("bioEn").GetString().Should().Be("I help with sleep and stress.");
        saved.GetProperty("currentPatients").GetInt32().Should().Be(2);

        // The patient-facing card uses it.
        using var factory = new ApiFactory(_sql) { ServerKey = _sql.ServerKey };
        using var reader = factory.ClientFor(p1.Id, AppRoles.Patient, p1.PseudonymId);
        reader.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        var card = await Read(await reader.GetAsync($"/v1/therapists/{t.Id}"));
        card.GetProperty("displayName").GetString().Should().Be("Huda T.");
        card.GetProperty("bio").GetString().Should().Be("I help with sleep and stress.");

        // Not accepting new patients: gone from the directory.
        await Read(await staff.PutAsJsonAsync("/v1/staff/profile", Body(capacity: 12, accepting: false)));
        (await Read(await reader.GetAsync("/v1/therapists"))).EnumerateArray().Should().NotContain(c => c.GetProperty("id").GetGuid() == t.Id);
    }

    [Fact]
    public async Task Only_an_admin_or_the_lead_verifies_the_licence_and_the_card_warns_30_days_before_expiry()
    {
        var t = await TherapistAsync();
        using var staff = _hosts.ApiClient("en", await SignInAsync(t));
        var path = $"/v1/staff/therapists/{t.Id}/license";
        var soon = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).AddDays(20);

        // The therapist and a supervisor may not; the lead may, with a future expiry date.
        (await staff.PutAsJsonAsync(path, new { verified = true, expiresOn = soon })).StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
        using var fahad = _hosts.ApiClient("en", await _hosts.StaffSignInAsync("fahad@staff.tamaneen.dev"));
        (await fahad.PutAsJsonAsync(path, new { verified = true, expiresOn = soon })).StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
        using var lead = _hosts.ApiClient("en", await _hosts.StaffSignInAsync("lead@staff.tamaneen.dev"));
        await Problem(await lead.PutAsJsonAsync(path, new { verified = true }), 400);
        await Problem(await lead.PutAsJsonAsync(path, new { verified = true, expiresOn = soon.AddDays(-30) }), 400);
        var set = await Read(await lead.PutAsJsonAsync(path, new { verified = true, expiresOn = soon }));
        set.GetProperty("verifiedBy").GetString().Should().Be("Dr. Hind S.");
        set.GetProperty("expiringSoon").GetBoolean().Should().BeTrue();

        var profile = await Read(await staff.GetAsync("/v1/staff/profile"));
        profile.GetProperty("licenseVerified").GetBoolean().Should().BeTrue();
        profile.GetProperty("licenseExpiresOn").GetString().Should().Be(soon.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        profile.GetProperty("licenseVerifiedBy").GetString().Should().Be("Dr. Hind S.");
        profile.GetProperty("licenseVerifiedAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        profile.GetProperty("licenseExpiringSoon").GetBoolean().Should().BeTrue();

        await Read(await lead.PutAsJsonAsync(path, new { verified = true, expiresOn = soon.AddDays(400) }));
        (await Read(await staff.GetAsync("/v1/staff/profile"))).GetProperty("licenseExpiringSoon").GetBoolean().Should().BeFalse();

        await using var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor));
        (await db.AuditEntries.CountAsync(e => e.Action == "therapist.license_verified" && e.EntityId == t.Id.ToString())).Should().Be(2);
    }

    [Fact]
    public async Task The_time_zone_is_chosen_from_the_list_and_the_schedule_follows_it()
    {
        var t = await TherapistAsync();
        using var staff = _hosts.ApiClient("en", await SignInAsync(t));
        var prefs = await Read(await staff.GetAsync("/v1/staff/me/preferences"));
        prefs.GetProperty("timeZone").GetString().Should().Be("Asia/Riyadh");
        prefs.GetProperty("timeZones").EnumerateArray().Select(z => z.GetString()).Should().Contain(["Asia/Dubai", "UTC"]);
        (await Problem(await staff.PutAsJsonAsync("/v1/staff/me/preferences", new { timeZone = "Mars/Olympus" }), 400)).GetProperty("code").GetString().Should().Be(ErrorCodes.InvalidTimeZone);
        (await Read(await staff.PutAsJsonAsync("/v1/staff/me/preferences", new { timeZone = "Asia/Dubai" }))).GetProperty("timeZone").GetString().Should().Be("Asia/Dubai");
        (await Read(await staff.GetAsync("/v1/staff/me"))).GetProperty("timeZone").GetString().Should().Be("Asia/Dubai");
        (await Read(await staff.GetAsync("/v1/staff/schedule?days=1"))).GetProperty("timeZone").GetString().Should().Be("Asia/Dubai");
    }

    [Fact]
    public async Task Alerts_and_the_inbox_show_my_patients_events_and_keep_unacknowledged_risk_on_top()
    {
        var t = await TherapistAsync();
        using var staff = _hosts.ApiClient("en", await SignInAsync(t));
        var mine = await _sql.CreatePatientAsync("mine");
        var other = await _sql.CreatePatientAsync("other");
        await _sql.AssignAsync(mine.Id, t.Id);
        var myEvent = await OpenEventAsync(mine);
        await OpenEventAsync(other);

        var alerts = await Read(await staff.GetAsync("/v1/staff/alerts?status=open"));
        alerts.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("eventId").GetGuid()).Should().Equal(myEvent);
        alerts.GetProperty("openCount").GetInt32().Should().Be(1);

        await _hosts.Api.Services.GetRequiredService<Notifier>().NotifyAsync(
            new Notification(t.Id, NotificationKinds.StaffAppointmentBooked, new Dictionary<string, string> { ["pseudonymId"] = mine.PseudonymId }), null, CancellationToken.None);
        var inbox = await Read(await staff.GetAsync("/v1/staff/inbox"));
        var items = inbox.GetProperty("items").EnumerateArray().ToList();
        items[0].GetProperty("pinned").GetBoolean().Should().BeTrue();
        items[0].GetProperty("category").GetString().Should().Be("risk");
        items[0].GetProperty("text").GetString().Should().Be($"High risk alert · {mine.PseudonymId} · not acknowledged yet");
        items[1].GetProperty("kind").GetString().Should().Be(NotificationKinds.StaffAppointmentBooked);
        items[1].GetProperty("link").GetString().Should().Be($"/patients/{mine.PseudonymId}");
        inbox.GetProperty("counts").GetProperty("risk").GetInt32().Should().Be(1);
        inbox.GetProperty("counts").GetProperty("appointments").GetInt32().Should().Be(1);

        await Read(await staff.PostAsync("/v1/staff/inbox/read-all", null));
        inbox = await Read(await staff.GetAsync("/v1/staff/inbox"));
        inbox.GetProperty("unreadCount").GetInt32().Should().Be(1, "the unacknowledged alert stays until someone acknowledges it");
        (await Read(await staff.GetAsync("/v1/staff/inbox?category=appointments"))).GetProperty("items").EnumerateArray().Should().OnlyContain(i => !i.GetProperty("pinned").GetBoolean());

        // Switching off "new booking or cancellation" stops those notices.
        await Read(await staff.PutAsJsonAsync("/v1/staff/me/notifications", new { riskAlerts = false, bookings = false, weeklyReport = true, dailyDigest = false }));
        var prefs = await Read(await staff.GetAsync("/v1/staff/me/notifications"));
        prefs.GetProperty("riskAlerts").GetBoolean().Should().BeTrue("risk alerts cannot be switched off");
        (await _hosts.Api.Services.GetRequiredService<Notifier>().NotifyAsync(
            new Notification(t.Id, NotificationKinds.StaffAppointmentCancelled, new Dictionary<string, string>()), null, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task Security_changes_need_a_step_up_and_the_authenticator_moves_only_once_the_new_device_answers()
    {
        var t = await TherapistAsync();
        var token = await SignInAsync(t);
        using var staff = _hosts.ApiClient("en", token);
        var security = await Read(await staff.GetAsync("/v1/staff/me/security"));
        security.GetProperty("totpEnabled").GetBoolean().Should().BeTrue();
        security.GetProperty("idleTimeoutMinutes").GetInt32().Should().Be(15);
        security.GetProperty("sessions").EnumerateArray().Should().Contain(s => s.GetProperty("current").GetBoolean());

        (await Problem(await staff.PostAsJsonAsync("/v1/staff/me/totp/move", new { password = Password, code = "000000" }), 400)).GetProperty("code").GetString().Should().Be(ErrorCodes.MfaFailed);
        (await Problem(await staff.PostAsJsonAsync("/v1/staff/me/totp/move", new { password = "wrong-password-1", code = t.Code() }), 400)).GetProperty("code").GetString().Should().Be(ErrorCodes.PasswordMismatch);

        var move = await Read(await staff.PostAsJsonAsync("/v1/staff/me/totp/move", new { password = Password, code = t.Code() }));
        var newSecret = Base32Decode(move.GetProperty("secret").GetString()!);
        var ticket = move.GetProperty("moveToken").GetString();

        // Until confirmed, the old device still works (recovery codes need a step-up with it).
        var codes = await Read(await staff.PostAsJsonAsync("/v1/staff/me/recovery-codes", new { password = Password, code = t.Code() }));
        codes.GetProperty("recoveryCodes").GetArrayLength().Should().Be(RecoveryCode.SetSize);

        await Problem(await staff.PostAsJsonAsync("/v1/staff/me/totp/move/confirm", new { moveToken = ticket + "x", code = Totp.Code(newSecret, Totp.StepAt(DateTimeOffset.UtcNow)) }), 410);
        await Read(await staff.PostAsJsonAsync("/v1/staff/me/totp/move/confirm", new { moveToken = ticket, code = Totp.Code(newSecret, Totp.StepAt(DateTimeOffset.UtcNow)) }));
        t.Secret = newSecret;
        t.LastStep = Totp.StepAt(DateTimeOffset.UtcNow);

        // A second session, then "end the others".
        var second = await SignInAsync(t);
        (await Read(await staff.GetAsync("/v1/staff/me/security"))).GetProperty("sessions").GetArrayLength().Should().Be(2);
        await Read(await staff.PostAsync("/v1/staff/me/sessions/revoke-others", null));
        using var revoked = _hosts.ApiClient("en", second);
        (await revoked.GetAsync("/v1/staff/me/security")).StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        (await Read(await staff.GetAsync("/v1/staff/me/security"))).GetProperty("sessions").GetArrayLength().Should().Be(1);
    }

    // ---------- helpers ----------

    private static object Body(int capacity = 10, string? displayNameAr = "أ. هدى ت.", string bioEn = "I help with sleep and stress.", bool accepting = true) => new
    {
        displayNameAr,
        displayNameEn = "Huda T.",
        titleAr = "أخصائية نفسية",
        titleEn = "Psychologist",
        bioAr = "أساعد في النوم والضغط.",
        bioEn,
        specialties = new[] { "sleep", "stress" },
        languages = new[] { "ar", "en" },
        capacity,
        acceptingNewPatients = accepting,
    };

    private async Task<Guid> OpenEventAsync(UserAccount patient)
    {
        await using var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.CrisisService));
        var evt = CrisisEvent.Open(patient.Id, patient.PseudonymId, RiskLevel.High, "ar", "test", TimeSpan.FromMinutes(2), _sql.Clock);
        db.CrisisEvents.Add(evt);
        await db.SaveChangesAsync();
        return evt.Id;
    }

    private sealed class TestTherapist
    {
        public required Guid Id { get; init; }

        public required string Email { get; init; }

        public required byte[] Secret { get; set; }

        public long LastStep { get; set; }

        /// <summary>A code for a step not used yet (each step is accepted once).</summary>
        public string Code()
        {
            var step = Math.Max(Totp.StepAt(DateTimeOffset.UtcNow) - 1, LastStep + 1);
            while (step > Totp.StepAt(DateTimeOffset.UtcNow) + 1)
            {
                Thread.Sleep(500);
            }

            LastStep = step;
            return Totp.Code(Secret, step);
        }
    }

    private async Task<TestTherapist> TherapistAsync()
    {
        var email = $"t-{Guid.NewGuid():N}@staff.example.com";
        var secret = Totp.NewSecret();
        var passwords = _hosts.Api.Services.GetRequiredService<PasswordService>();
        var secrets = _hosts.Api.Services.GetRequiredService<ServerSecrets>();
        var account = UserAccount.Register(AppRoles.Therapist, new ContactHasher(_sql.Pepper).Hash(email), "ar", _sql.Clock);
        account.Activate();
        await using var db = _sql.Context(StaticCurrentUser.Anonymous);
        db.UserAccounts.Add(account);
        var credential = Credential.Create(account.Id, passwords.Hash(Password), _sql.Clock);
        credential.BeginTotpSetup(secrets.Protect(AuthService.TotpPurpose, Convert.ToBase64String(secret)));
        credential.EnableTotp(Totp.StepAt(DateTimeOffset.UtcNow) - 10);
        db.Credentials.Add(credential);
        db.StaffProfiles.Add(StaffProfile.Create(account.Id, "أ. هدى", "Huda", null));
        db.TherapistProfiles.Add(TherapistProfile.Create(account.Id, "أخصائية", "Psychologist", "نبذة", "Bio", "female", ["sleep"], ["ar"], licenseVerified: true, capacity: 10));
        await db.SaveChangesAsync();
        return new TestTherapist { Id = account.Id, Email = email, Secret = secret };
    }

    private async Task<string> SignInAsync(TestTherapist t)
    {
        using var client = _hosts.ApiClient("en");
        var login = await Read(await client.PostAsJsonAsync("/v1/auth/login", new { contact = t.Email, password = Password, client = "staff" }));
        var mfa = await Read(await client.PostAsJsonAsync("/v1/auth/login/mfa", new { mfaToken = login.GetProperty("mfaToken").GetString(), method = "totp", code = t.Code(), rememberDevice = false }));
        return mfa.GetProperty("tokens").GetProperty("accessToken").GetString()!;
    }

    private static byte[] Base32Decode(string s)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var value = 0;
        var output = new List<byte>();
        foreach (var c in s.TrimEnd('=').ToUpperInvariant())
        {
            value = (value << 5) | alphabet.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((value >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. output];
    }
}
