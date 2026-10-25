using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Identity;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Clinical;

public sealed record TherapistSummaryDto(
    Guid Id,
    string DisplayName,
    string Initials,
    string Title,
    IReadOnlyList<string> Specialties,
    IReadOnlyList<string> Languages,
    string Gender,
    bool LicenseVerified,
    DateTimeOffset? NextAvailableAt,
    bool HasEvening);

public sealed record TherapistProfileDto(
    Guid Id,
    string DisplayName,
    string Initials,
    string Title,
    IReadOnlyList<string> Specialties,
    IReadOnlyList<string> Languages,
    string Gender,
    bool LicenseVerified,
    DateTimeOffset? NextAvailableAt,
    bool HasEvening,
    string Bio,
    string SessionType,
    int DurationMinutes);

/// <summary>Builds therapist cards (P07, P24, P25) and computes match suggestions after a triage review.</summary>
public sealed class TherapistDirectory(TamaneenDbContext db, IClock clock, IOptions<PlatformOptions> platform)
{
    /// <summary>Evening means a session starting at 17:00 or later in the pilot's time zone.</summary>
    public const int EveningHour = 17;

    /// <summary>Bookable times start at least this far ahead, so the therapist can read the file first.</summary>
    public static readonly TimeSpan MinimumNotice = TimeSpan.FromHours(2);

    private static readonly string[] Honorifics = ["أ.", "د.", "Dr.", "Ms.", "Mr.", "Mrs."];

    public async Task<List<TherapistSummaryDto>> ListAsync(string language, IEnumerable<Guid>? only, CancellationToken ct)
    {
        var query = db.TherapistProfiles.AsNoTracking().Where(t => t.AcceptingNewPatients);
        if (only is not null)
        {
            var ids = only.ToList();
            query = db.TherapistProfiles.AsNoTracking().Where(t => ids.Contains(t.UserId));
        }

        var profiles = await query.ToListAsync(ct).ConfigureAwait(false);
        var ids2 = profiles.Select(p => p.UserId).ToList();
        var names = await db.StaffProfiles.AsNoTracking().Where(s => ids2.Contains(s.UserId)).ToDictionaryAsync(s => s.UserId, ct).ConfigureAwait(false);
        var now = clock.UtcNow;
        var earliest = now + MinimumNotice;
        var slots = await db.AvailabilitySlots.AsNoTracking()
            .Where(s => ids2.Contains(s.TherapistUserId) && s.StartsAtUtc > earliest && (s.Status == SlotStatus.Open || (s.Status == SlotStatus.Held && s.HeldUntilUtc <= now)))
            .Select(s => new { s.TherapistUserId, s.StartsAtUtc })
            .ToListAsync(ct).ConfigureAwait(false);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(platform.Value.TimeZone);
        return profiles.Select(p =>
        {
            var mine = slots.Where(s => s.TherapistUserId == p.UserId).Select(s => s.StartsAtUtc).ToList();
            var name = names.TryGetValue(p.UserId, out var staff) ? staff.DisplayName(language) : string.Empty;
            return new TherapistSummaryDto(
                p.UserId,
                name,
                Initials(name),
                p.Title(language),
                p.SpecialtyList,
                p.LanguageList,
                p.Gender,
                p.LicenseVerified,
                mine.Count == 0 ? null : mine.Min(),
                mine.Any(s => TimeZoneInfo.ConvertTime(s, zone).Hour >= EveningHour));
        }).OrderBy(t => t.NextAvailableAt ?? DateTimeOffset.MaxValue).ToList();
    }

    public async Task<TherapistProfileDto> GetAsync(Guid id, string language, CancellationToken ct)
    {
        var profile = await db.TherapistProfiles.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == id, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Therapist {id} not found.");
        var s = (await ListAsync(language, [id], ct).ConfigureAwait(false)).Single();
        return new TherapistProfileDto(s.Id, s.DisplayName, s.Initials, s.Title, s.Specialties, s.Languages, s.Gender, s.LicenseVerified, s.NextAvailableAt, s.HasEvening, profile.Bio(language), "remote", profile.SessionMinutes);
    }

    /// <summary>
    /// Ranks therapists for a patient: shared focus areas with what the patient described, the patient's
    /// language, spare capacity and the soonest free time. The reviewing supervisor sees and approves the result.
    /// </summary>
    public async Task<IReadOnlyList<(Guid TherapistId, IReadOnlyList<string> Shared)>> SuggestAsync(IReadOnlyCollection<string> focus, string patientLanguage, int count, CancellationToken ct)
    {
        var therapists = await ListAsync(patientLanguage, null, ct).ConfigureAwait(false);
        var load = await db.CareAssignments.AsNoTracking().Where(a => a.EndedAtUtc == null).GroupBy(a => a.TherapistUserId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct).ConfigureAwait(false);
        var capacity = await db.TherapistProfiles.AsNoTracking().ToDictionaryAsync(t => t.UserId, t => t.Capacity, ct).ConfigureAwait(false);
        var now = clock.UtcNow;
        return therapists
            .Where(t => t.NextAvailableAt is not null && load.GetValueOrDefault(t.Id) < capacity.GetValueOrDefault(t.Id, 0))
            .Select(t =>
            {
                var shared = t.Specialties.Where(focus.Contains).ToList();
                var days = (t.NextAvailableAt!.Value - now).TotalDays;
                var score = (shared.Count * 3) + (t.Languages.Contains(patientLanguage) ? 4 : -10) - Math.Min(days, 14) * 0.3;
                return (t.Id, Shared: (IReadOnlyList<string>)shared, Score: score);
            })
            .OrderByDescending(x => x.Score)
            .Take(count)
            .Select(x => (x.Id, x.Shared))
            .ToList();
    }

    /// <summary>
    /// Therapists a supervisor may propose (S03): licence verified, accepting new patients, a free slot ahead and
    /// spare capacity. Each comes with its current load and capacity.
    /// </summary>
    public async Task<List<(TherapistSummaryDto Therapist, int Load, int Capacity)>> AvailableAsync(string language, CancellationToken ct)
    {
        var therapists = await ListAsync(language, null, ct).ConfigureAwait(false);
        var load = await db.CareAssignments.AsNoTracking().Where(a => a.EndedAtUtc == null).GroupBy(a => a.TherapistUserId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct).ConfigureAwait(false);
        var capacity = await db.TherapistProfiles.AsNoTracking().ToDictionaryAsync(t => t.UserId, t => t.Capacity, ct).ConfigureAwait(false);
        return therapists
            .Where(t => t.LicenseVerified && t.NextAvailableAt is not null && load.GetValueOrDefault(t.Id) < capacity.GetValueOrDefault(t.Id, 0))
            .Select(t => (t, load.GetValueOrDefault(t.Id), capacity.GetValueOrDefault(t.Id, 0)))
            .ToList();
    }

    /// <summary>"أ. سارة م." → "س م"; "Dr. Sara M." → "S M".</summary>
    public static string Initials(string displayName)
    {
        var words = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !Honorifics.Contains(w)).Take(2);
        return string.Join(' ', words.Select(w => w.TrimStart('(')[..1].ToUpperInvariant()));
    }
}
