using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tamaneen.Api.Auth;
using Tamaneen.Api.Endpoints;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Hosting;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;
using static Tamaneen.IntegrationTests.JourneyHosts;

namespace Tamaneen.IntegrationTests;

/// <summary>
/// P56 waitlist through the real API: join with preferences, the place is the patient's alone (RLS), a freed time
/// that fits notifies the first patient in the queue (message and in-app offer), and the patient can leave.
/// The seeded therapists have no Friday or Saturday times, so a weekend-only entry waits until one appears.
/// </summary>
[Collection(SqlServerGroup.Name)]
public sealed class WaitlistTests : IAsyncLifetime
{
    private const string Password = "Quiet-Morning-Walk-77";
    private readonly SqlServerFixture _sql;
    private JourneyHosts _hosts = null!;

    public WaitlistTests(SqlServerFixture sql) => _sql = sql;

    public Task InitializeAsync()
    {
        _hosts = new JourneyHosts(_sql);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _hosts.DisposeAsync();

    [Fact]
    public async Task A_freed_time_that_fits_notifies_the_waiting_patient_who_can_then_leave()
    {
        var (waiter, waiterEmail) = await BookablePatientAsync("ar");
        var (other, _) = await BookablePatientAsync("en");
        var therapists = await Read(await waiter.GetAsync("/v1/therapists"));
        var sara = therapists.EnumerateArray().First(t => t.GetProperty("gender").GetString() == "female");
        var saraId = sara.GetProperty("id").GetGuid();

        (await Read(await waiter.GetAsync("/v1/me/waitlist"))).GetProperty("status").GetString().Should().Be("none");
        (await Problem(await waiter.PutAsJsonAsync("/v1/me/waitlist", new { gender = "robot", timesOfDay = new[] { "midnight" } }), 400))
            .GetProperty("errors").TryGetProperty("gender", out _).Should().BeTrue();

        var joined = await Read(await waiter.PutAsJsonAsync("/v1/me/waitlist", new { therapistId = saraId, gender = "female", language = "ar", timesOfDay = new[] { "morning" }, days = new[] { "weekend" } }));
        joined.GetProperty("status").GetString().Should().Be("waiting");
        joined.GetProperty("expectedDaysMin").GetInt32().Should().Be(3);
        joined.GetProperty("preferences").GetProperty("days").EnumerateArray().Select(d => d.GetString()).Should().Equal("weekend");
        (await Read(await other.GetAsync("/v1/me/waitlist"))).GetProperty("status").GetString().Should().Be("none", "row-level security keeps the entry to its patient");

        // A Friday-morning time appears for Sara and someone else holds it first: nobody is told yet.
        var friday = await AddFridayMorningSlotAsync(saraId);
        var hold = await Read(await other.PostAsJsonAsync("/v1/bookings/holds", new { slotId = friday }));
        (await Read(await waiter.GetAsync("/v1/me/waitlist"))).GetProperty("status").GetString().Should().Be("waiting");

        // The hold is released: the time is free again and fits the entry.
        await Read(await other.DeleteAsync($"/v1/bookings/holds/{hold.GetProperty("holdId").GetGuid()}"));
        var available = await Read(await waiter.GetAsync("/v1/me/waitlist"));
        available.GetProperty("status").GetString().Should().Be("available");
        available.GetProperty("offer").GetProperty("therapistId").GetGuid().Should().Be(saraId);
        var message = _hosts.Outbox.LastTo(waiterEmail);
        message.Should().NotBeNull();
        message!.Purpose.Should().Be(MessagePurpose.WaitlistSlotOpen);
        message.Text.Should().Contain("توفر موعد");

        await Read(await waiter.DeleteAsync("/v1/me/waitlist"));
        (await Read(await waiter.GetAsync("/v1/me/waitlist"))).GetProperty("status").GetString().Should().Be("none");
        (await Read(await waiter.PutAsJsonAsync("/v1/me/waitlist", new { days = new[] { "weekend" }, timesOfDay = new[] { "evening" } })))
            .GetProperty("status").GetString().Should().Be("waiting", "a patient can join again after leaving");
    }

    [Fact]
    public async Task Joining_needs_a_patient_who_may_book()
    {
        using var anon = _hosts.ApiClient("en");
        var email = $"wl-{Guid.NewGuid():N}@example.com";
        var eligibility = await Read(await anon.PostAsJsonAsync("/v1/auth/eligibility", new { isAdult = true, cityCode = "RUH" }));
        var challenge = await Read(await anon.PostAsJsonAsync("/v1/auth/signup", new { eligibilityToken = eligibility.GetProperty("eligibilityToken").GetString(), nickname = "Calm Palm", contactType = "email", contact = email, password = Password, language = "en" }));
        var verified = await Read(await anon.PostAsJsonAsync("/v1/auth/signup/verify", new { challengeId = challenge.GetProperty("challengeId").GetGuid(), code = _hosts.CodeSentTo(email) }));
        using var me = _hosts.ApiClient("en", verified.GetProperty("tokens").GetProperty("accessToken").GetString());
        (await Problem(await me.PutAsJsonAsync("/v1/me/waitlist", new { days = new[] { "weekend" } }), 403))
            .GetProperty("code").GetString().Should().Be(ErrorCodes.BookingNotEligible);
    }

    private async Task<Guid> AddFridayMorningSlotAsync(Guid therapistId)
    {
        await using var scope = _hosts.Api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestCurrentUser>().ActAsService(SchedulingEndpoints.SchedulingServiceId, AppRoles.SchedulingService);
        var db = scope.ServiceProvider.GetRequiredService<TamaneenDbContext>();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        var day = Enumerable.Range(1, 7).Select(today.AddDays).First(d => d.DayOfWeek == DayOfWeek.Friday);
        var local = day.ToDateTime(new TimeOnly(10, 0));
        var slot = AvailabilitySlot.Create(therapistId, new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime(), 50);
        db.AvailabilitySlots.Add(slot);
        await db.SaveChangesAsync();
        (await db.AvailabilitySlots.CountAsync(s => s.Id == slot.Id)).Should().Be(1);
        return slot.Id;
    }

    /// <summary>Consents, safety info, the direct path and a negative safety check: the patient may book.</summary>
    private async Task<(HttpClient Client, string Email)> BookablePatientAsync(string language)
    {
        var email = $"wl-{Guid.NewGuid():N}@example.com";
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
