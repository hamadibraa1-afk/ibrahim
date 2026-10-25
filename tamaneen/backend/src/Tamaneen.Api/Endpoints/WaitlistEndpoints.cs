using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamaneen.Api.Clinical;
using Tamaneen.Api.Patients;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Hosting;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Endpoints;

/// <summary>P56 waitlist: join with preferences, change them, leave; the notice arrives when a fitting time opens.</summary>
public static class WaitlistEndpoints
{
    public sealed record WaitlistPreferencesDto(string Gender, string Language, IReadOnlyList<string> TimesOfDay, IReadOnlyList<string> Days);

    /// <summary>The time that opened, offered on P56 with a link to book it (P08).</summary>
    public sealed record WaitlistOfferDto(Guid TherapistId, string TherapistName, DateTimeOffset StartsAt);

    /// <summary>Status is none, waiting or available. ExpectedDays is the platform's current estimate (S50 later).</summary>
    public sealed record WaitlistDto(
        string Status,
        Guid? TherapistId,
        string? TherapistName,
        WaitlistPreferencesDto? Preferences,
        DateTimeOffset? JoinedAt,
        int ExpectedDaysMin,
        int ExpectedDaysMax,
        WaitlistOfferDto? Offer);

    public sealed record JoinWaitlistRequest(Guid? TherapistId, string? Gender, string? Language, List<string>? TimesOfDay, List<string>? Days);

    public static IEndpointRouteBuilder MapWaitlistEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/me/waitlist").RequireAuthorization(AuthSetup.PatientPolicy);
        g.MapGet("", GetAsync);
        g.MapPut("", JoinAsync);
        g.MapDelete("", LeaveAsync);
        return app;
    }

    internal static async Task<Ok<WaitlistDto>> GetAsync(PatientContext p, TherapistDirectory directory, IOptions<PlatformOptions> platform, CancellationToken ct) =>
        TypedResults.Ok(await BuildAsync(p, directory, platform.Value, ct).ConfigureAwait(false));

    /// <summary>Joins, or changes the preferences of the patient's entry (the place in the queue stays). Then checks the free times at once.</summary>
    internal static async Task<Ok<WaitlistDto>> JoinAsync(JoinWaitlistRequest request, PatientContext p, TherapistDirectory directory, WaitlistNotifier notifier, IAuditLog audit, IOptions<PlatformOptions> platform, IClock clock, CancellationToken ct)
    {
        await SchedulingEndpoints.EnsureCanBookAsync(p, ct).ConfigureAwait(false);
        if (request.TherapistId is { } therapistId && !await p.Db.TherapistProfiles.AnyAsync(t => t.UserId == therapistId, ct).ConfigureAwait(false))
        {
            throw new DomainException(ErrorCodes.NotFound, $"Therapist {therapistId} not found.");
        }

        var profile = await p.ProfileAsync(ct).ConfigureAwait(false);
        var risk = await RiskAsync(p, ct).ConfigureAwait(false);
        var entry = await ActiveAsync(p, ct).ConfigureAwait(false);
        if (entry is null)
        {
            entry = WaitlistEntry.Join(p.PatientId, request.TherapistId, request.Gender, request.Language, request.TimesOfDay, request.Days, risk, profile.TimeZone, clock);
            p.Db.WaitlistEntries.Add(entry);
            audit.Append("waitlist.joined", "WaitlistEntry", entry.Id.ToString(), profile.PseudonymId);
        }
        else
        {
            entry.SetPreferences(request.TherapistId, request.Gender, request.Language, request.TimesOfDay, request.Days, risk, profile.TimeZone);
            audit.Append("waitlist.preferences_changed", "WaitlistEntry", entry.Id.ToString(), profile.PseudonymId);
        }

        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        await notifier.CheckAsync(ct).ConfigureAwait(false);
        await p.Db.Entry(entry).ReloadAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(await BuildAsync(p, directory, platform.Value, ct).ConfigureAwait(false));
    }

    internal static async Task<NoContent> LeaveAsync(PatientContext p, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var entry = await ActiveAsync(p, ct).ConfigureAwait(false);
        if (entry is not null)
        {
            entry.Leave(clock);
            audit.Append("waitlist.left", "WaitlistEntry", entry.Id.ToString(), (await p.AccountAsync(ct).ConfigureAwait(false)).PseudonymId);
            await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return TypedResults.NoContent();
    }

    private static Task<WaitlistEntry?> ActiveAsync(PatientContext p, CancellationToken ct) =>
        p.Db.WaitlistEntries.FirstOrDefaultAsync(w => w.PatientId == p.PatientId && w.Status != WaitlistStatus.Left, ct);

    /// <summary>The queue is ordered by the latest reviewed level: the triage case, else the direct-path safety check.</summary>
    private static async Task<RiskLevel> RiskAsync(PatientContext p, CancellationToken ct)
    {
        var triage = await p.Db.TriageCases.Where(t => t.PatientId == p.PatientId).OrderByDescending(t => t.SubmittedAtUtc).Select(t => (RiskLevel?)t.RiskLevel).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (triage is { } level)
        {
            return level;
        }

        var screening = await p.Db.Screenings.Where(s => s.PatientId == p.PatientId).OrderByDescending(s => s.CompletedAtUtc).Select(s => (RiskLevel?)s.Level).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return screening ?? RiskLevel.Routine;
    }

    private static async Task<WaitlistDto> BuildAsync(PatientContext p, TherapistDirectory directory, PlatformOptions platform, CancellationToken ct)
    {
        var entry = await p.Db.WaitlistEntries.AsNoTracking().FirstOrDefaultAsync(w => w.PatientId == p.PatientId && w.Status != WaitlistStatus.Left, ct).ConfigureAwait(false);
        if (entry is null)
        {
            return new WaitlistDto("none", null, null, null, null, platform.WaitlistExpectedDaysMin, platform.WaitlistExpectedDaysMax, null);
        }

        var ids = new[] { entry.TherapistUserId }.OfType<Guid>().ToList();
        var slot = entry.NotifiedSlotId is { } slotId
            ? await p.Db.AvailabilitySlots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == slotId, ct).ConfigureAwait(false)
            : null;
        if (slot is not null)
        {
            ids.Add(slot.TherapistUserId);
        }

        var names = ids.Count == 0 ? [] : (await directory.ListAsync(p.Language, ids.Distinct(), ct).ConfigureAwait(false)).ToDictionary(t => t.Id, t => t.DisplayName);
        var offer = entry.Status == WaitlistStatus.Notified && slot is not null
            ? new WaitlistOfferDto(slot.TherapistUserId, names.GetValueOrDefault(slot.TherapistUserId, string.Empty), slot.StartsAtUtc)
            : null;
        return new WaitlistDto(
            entry.Status == WaitlistStatus.Notified ? "available" : "waiting",
            entry.TherapistUserId,
            entry.TherapistUserId is { } t ? names.GetValueOrDefault(t) : null,
            new WaitlistPreferencesDto(entry.Gender, entry.Language, entry.TimeList, entry.DayList),
            entry.JoinedAtUtc,
            platform.WaitlistExpectedDaysMin,
            platform.WaitlistExpectedDaysMax,
            offer);
    }
}
