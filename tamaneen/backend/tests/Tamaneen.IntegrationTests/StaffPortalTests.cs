using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Tamaneen.Api.Dev;
using Tamaneen.SharedKernel;
using static Tamaneen.IntegrationTests.JourneyHosts;

namespace Tamaneen.IntegrationTests;

/// <summary>S06 around sign-in: staff password recovery and the audited forbidden page.</summary>
[Collection(SqlServerGroup.Name)]
public sealed class StaffPortalTests : IAsyncLifetime
{
    private readonly SqlServerFixture _sql;
    private JourneyHosts _hosts = null!;

    public StaffPortalTests(SqlServerFixture sql) => _sql = sql;

    public Task InitializeAsync()
    {
        _hosts = new JourneyHosts(_sql);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _hosts.DisposeAsync();

    [Fact]
    public async Task Staff_reset_a_forgotten_password_by_email_code_and_still_need_the_authenticator()
    {
        const string email = "mona@staff.tamaneen.dev";
        const string temporary = "Quiet-Garden-Path-2026";
        using var en = _hosts.ApiClient("en");
        await _hosts.StaffSignInAsync(email); // seeds the platform and proves the seeded password works

        var real = await Read(await en.PostAsJsonAsync("/v1/auth/recover", new { contact = email }));
        var fake = await Read(await en.PostAsJsonAsync("/v1/auth/recover", new { contact = $"ghost-{Guid.NewGuid():N}@staff.tamaneen.dev" }));
        real.EnumerateObject().Select(p => p.Name).Should().Equal(fake.EnumerateObject().Select(p => p.Name));
        await Read(await en.PostAsJsonAsync("/v1/auth/recover/complete", new { challengeId = real.GetProperty("challengeId").GetGuid(), code = _hosts.CodeSentTo(email), newPassword = temporary }));

        await Problem(await en.PostAsJsonAsync("/v1/auth/login", new { contact = email, password = DevSeeder.StaffPassword, client = "staff" }), 401);
        var login = await Read(await en.PostAsJsonAsync("/v1/auth/login", new { contact = email, password = temporary, client = "staff" }));
        login.GetProperty("status").GetString().Should().Be("mfa", "recovery never skips or resets two-step verification");

        // Put the seeded password back for the other tests.
        var again = await Read(await en.PostAsJsonAsync("/v1/auth/recover", new { contact = email }));
        await Read(await en.PostAsJsonAsync("/v1/auth/recover/complete", new { challengeId = again.GetProperty("challengeId").GetGuid(), code = _hosts.CodeSentTo(email), newPassword = DevSeeder.StaffPassword }));
    }

    [Fact]
    public async Task A_refused_portal_page_is_written_to_the_audit_ledger()
    {
        using var noura = _hosts.ApiClient("en", await _hosts.StaffSignInAsync("noura@staff.tamaneen.dev"));
        await Problem(await noura.PostAsJsonAsync("/v1/staff/access-denied", new { path = "" }), 400);
        await Read(await noura.PostAsJsonAsync("/v1/staff/access-denied", new { path = "/crisis/0b4f" }));

        using var anonymous = _hosts.ApiClient("en");
        await Problem(await anonymous.PostAsJsonAsync("/v1/staff/access-denied", new { path = "/crisis" }), 401);

        await using var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor));
        var entry = await db.AuditEntries.Where(a => a.Action == "staff.access_denied").OrderByDescending(a => a.OccurredAtUtc).FirstAsync();
        entry.Reason.Should().Be("/crisis/0b4f");
        entry.ActorRole.Should().Be(AppRoles.Therapist);
    }
}
