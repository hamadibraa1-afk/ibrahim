using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Hosting;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Endpoints;

/// <summary>
/// S21 «الملف المهني»: the therapist edits what patients see on the directory cards (P07, P24, P25) and what
/// matching uses: the display name, title and bio in Arabic and English, specialties, session languages,
/// capacity and "accepting new patients". The guide's approval tier (PendingChange, lead approval) covers
/// safety content only, so these save directly; licence details are not editable here (a new verification, S09).
/// Patients see only the name, title, bio, specialties and languages. No phone numbers, e-mails or links in the
/// text (S21 rule).
/// </summary>
public static partial class StaffProfileEndpoints
{
    public const int MaxDisplayName = 60;
    public const int MaxTitle = 80;
    public const int MaxBio = 600;

    private static readonly string[] ContactDetailsError = ["contact_details"];

    public sealed record StaffProfileDto(
        string DisplayNameAr,
        string DisplayNameEn,
        string TitleAr,
        string TitleEn,
        string BioAr,
        string BioEn,
        IReadOnlyList<string> Specialties,
        IReadOnlyList<string> Languages,
        string Gender,
        string SessionType,
        int SessionMinutes,
        int Capacity,
        int CurrentPatients,
        bool AcceptingNewPatients,
        bool LicenseVerified,
        IReadOnlyList<string> SpecialtyCatalog,
        DateOnly? LicenseExpiresOn,
        string? LicenseVerifiedBy,
        DateTimeOffset? LicenseVerifiedAt,
        bool LicenseExpiringSoon);

    public sealed record LicenseRequest(bool? Verified, DateOnly? ExpiresOn);

    public sealed record LicenseDto(Guid UserId, bool Verified, DateOnly? ExpiresOn, string? VerifiedBy, DateTimeOffset? VerifiedAt, bool ExpiringSoon);

    public sealed record UpdateStaffProfileRequest(
        string? DisplayNameAr,
        string? DisplayNameEn,
        string? TitleAr,
        string? TitleEn,
        string? BioAr,
        string? BioEn,
        List<string>? Specialties,
        List<string>? Languages,
        int? Capacity,
        bool? AcceptingNewPatients);

    public static IEndpointRouteBuilder MapStaffProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/staff/profile").RequireAuthorization(p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Therapist));
        g.MapGet("", GetAsync);
        g.MapPut("", UpdateAsync);

        // Licence verification: an admin or the clinical lead only (the S05 / S09 screens come later; this is the API they use).
        app.MapPut("/v1/staff/therapists/{userId:guid}/license", SetLicenseAsync)
            .RequireAuthorization(p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Admin, AppRoles.ClinicalLead));
        return app;
    }

    internal static async Task<Ok<StaffProfileDto>> GetAsync(ICurrentUser user, TamaneenDbContext db, IOptions<PlatformOptions> platform, IClock clock, CancellationToken ct) =>
        TypedResults.Ok(await LoadAsync(db, user, Today(platform, clock), ct).ConfigureAwait(false));

    /// <summary>Verifies the licence with its expiry date (in the future), or withdraws the verification. Audited.</summary>
    internal static async Task<Ok<LicenseDto>> SetLicenseAsync(Guid userId, LicenseRequest request, ICurrentUser user, TamaneenDbContext db, IAuditLog audit, IOptions<PlatformOptions> platform, IClock clock, CancellationToken ct)
    {
        var today = Today(platform, clock);
        if (request.Verified is not { } verified)
        {
            throw MeEndpoints.Invalid("verified");
        }

        if (verified && (request.ExpiresOn is null || request.ExpiresOn <= today))
        {
            throw MeEndpoints.Invalid("expiresOn");
        }

        var therapist = await db.TherapistProfiles.FirstOrDefaultAsync(t => t.UserId == userId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, "No therapist profile.");
        therapist.SetLicenseVerification(verified, request.ExpiresOn, today, user.UserId!.Value, clock);
        audit.Append(verified ? "therapist.license_verified" : "therapist.license_unverified", "TherapistProfile", userId.ToString(), null, therapist.LicenseExpiresOn is { } e ? $"expires={e:yyyy-MM-dd}" : null);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var by = await VerifierNameAsync(db, therapist, user.Language, ct).ConfigureAwait(false);
        return TypedResults.Ok(new LicenseDto(userId, therapist.LicenseVerified, therapist.LicenseExpiresOn, by, therapist.LicenseVerifiedAtUtc, therapist.LicenseExpiringSoon(today)));
    }

    private static DateOnly Today(IOptions<PlatformOptions> platform, IClock clock) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, AvailabilityEndpoints.Zone(platform.Value)).DateTime);

    private static async Task<string?> VerifierNameAsync(TamaneenDbContext db, TherapistProfile t, string language, CancellationToken ct) =>
        t.LicenseVerifiedByUserId is { } by
            ? (await db.StaffProfiles.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == by, ct).ConfigureAwait(false))?.DisplayName(language)
            : null;

    internal static async Task<Ok<StaffProfileDto>> UpdateAsync(UpdateStaffProfileRequest request, ICurrentUser user, TamaneenDbContext db, IAuditLog audit, IOptions<PlatformOptions> platform, IClock clock, CancellationToken ct)
    {
        var me = user.UserId!.Value;
        var therapist = await db.TherapistProfiles.FirstOrDefaultAsync(t => t.UserId == me, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, "No therapist profile.");
        var staff = await db.StaffProfiles.FirstOrDefaultAsync(s => s.UserId == me, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, "No staff profile.");

        var errors = new Dictionary<string, string[]>();
        string Text(string field, string? value, int max, bool required)
        {
            var v = value?.Trim() ?? string.Empty;
            if (required && v.Length == 0)
            {
                errors[field] = ["required"];
            }
            else if (v.Length > max)
            {
                errors[field] = ["too_long"];
            }

            return v;
        }

        var nameAr = Text("displayNameAr", request.DisplayNameAr, MaxDisplayName, true);
        var nameEn = Text("displayNameEn", request.DisplayNameEn, MaxDisplayName, true);
        var titleAr = Text("titleAr", request.TitleAr, MaxTitle, false);
        var titleEn = Text("titleEn", request.TitleEn, MaxTitle, false);
        var bioAr = Text("bioAr", request.BioAr, MaxBio, false);
        var bioEn = Text("bioEn", request.BioEn, MaxBio, false);
        var specialties = request.Specialties?.Select(s => s.Trim()).Distinct().ToList() ?? [];
        if (specialties.Count == 0 || specialties.Any(s => !TherapistProfile.SpecialtyCodes.Contains(s)))
        {
            errors["specialties"] = [specialties.Count == 0 ? "required" : "invalid"];
        }

        var languages = request.Languages?.Select(l => l.Trim()).Distinct().ToList() ?? [];
        if (languages.Count == 0 || languages.Any(l => !SupportedLanguages.IsSupported(l)))
        {
            errors["languages"] = [languages.Count == 0 ? "required" : "invalid"];
        }

        if (request.Capacity is not { } capacity || capacity is <= 0 or > 200)
        {
            errors["capacity"] = ["invalid"];
            capacity = 0;
        }

        if (errors.Count > 0)
        {
            throw new DomainException(ErrorCodes.Validation, "Profile invalid.").With("errors", errors);
        }

        var contact = new[] { ("displayNameAr", nameAr), ("displayNameEn", nameEn), ("titleAr", titleAr), ("titleEn", titleEn), ("bioAr", bioAr), ("bioEn", bioEn) }
            .Where(f => HasContactDetails(f.Item2)).Select(f => f.Item1).ToList();
        if (contact.Count > 0)
        {
            throw new DomainException(ErrorCodes.ProfileContactDetails, "Contact details in profile text.")
                .With("errors", contact.ToDictionary(f => f, _ => ContactDetailsError));
        }

        var current = await CurrentPatientsAsync(db, me, ct).ConfigureAwait(false);
        therapist.SetCapacity(capacity, current);
        therapist.Update(titleAr, titleEn, bioAr, bioEn, therapist.Gender, specialties, languages);
        therapist.SetAcceptingNewPatients(request.AcceptingNewPatients ?? therapist.AcceptingNewPatients);
        staff.Rename(nameAr, nameEn);
        audit.Append("therapist.profile_updated", "TherapistProfile", me.ToString(), null, $"capacity={capacity};accepting={therapist.AcceptingNewPatients}");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(await LoadAsync(db, user, Today(platform, clock), ct).ConfigureAwait(false));
    }

    /// <summary>A phone number (7+ digits, Latin or Arabic-Indic), an e-mail address or a link.</summary>
    public static bool HasContactDetails(string text) => ContactPattern().IsMatch(text);

    private static Task<int> CurrentPatientsAsync(TamaneenDbContext db, Guid me, CancellationToken ct) =>
        db.CareAssignments.CountAsync(a => a.TherapistUserId == me && a.EndedAtUtc == null, ct);

    private static async Task<StaffProfileDto> LoadAsync(TamaneenDbContext db, ICurrentUser user, DateOnly today, CancellationToken ct)
    {
        var me = user.UserId!.Value;
        var t = await db.TherapistProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == me, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, "No therapist profile.");
        var s = await db.StaffProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == me, ct).ConfigureAwait(false);
        var current = await CurrentPatientsAsync(db, me, ct).ConfigureAwait(false);
        return new StaffProfileDto(
            s?.DisplayNameAr ?? string.Empty,
            s?.DisplayNameEn ?? string.Empty,
            t.TitleAr,
            t.TitleEn,
            t.BioAr,
            t.BioEn,
            t.SpecialtyList,
            t.LanguageList,
            t.Gender,
            "remote",
            t.SessionMinutes,
            t.Capacity,
            current,
            t.AcceptingNewPatients,
            t.LicenseVerified,
            TherapistProfile.SpecialtyCodes.Order(StringComparer.Ordinal).ToList(),
            t.LicenseExpiresOn,
            await VerifierNameAsync(db, t, user.Language, ct).ConfigureAwait(false),
            t.LicenseVerifiedAtUtc,
            t.LicenseExpiringSoon(today));
    }

    [GeneratedRegex(@"([0-9٠-٩][\s\-\.]?){7,}|[^\s@]+@[^\s@]+\.[^\s@]+|https?://|www\.|\b[a-z0-9-]+\.(com|net|org|sa|io|me)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ContactPattern();
}
