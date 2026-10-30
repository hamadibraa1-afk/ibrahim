using Tamaneen.SharedKernel;

namespace Tamaneen.Identity;

/// <summary>Name of a staff member as patients and colleagues see it, in both languages.</summary>
public sealed class StaffProfile
{
    private StaffProfile(Guid userId, string displayNameAr, string displayNameEn)
    {
        UserId = userId;
        DisplayNameAr = displayNameAr;
        DisplayNameEn = displayNameEn;
    }

    // EF Core
    private StaffProfile()
    {
        DisplayNameAr = string.Empty;
        DisplayNameEn = string.Empty;
    }

    public Guid UserId { get; private set; }

    public string DisplayNameAr { get; private set; }

    public string DisplayNameEn { get; private set; }

    /// <summary>On-call order for crisis paging: 0 primary, 1 backup, null not on the rota.</summary>
    public byte? OnCallTier { get; private set; }

    /// <summary>S32: the zone the portal shows times in (IANA id). The platform's zone until the person picks another.</summary>
    public string TimeZone { get; private set; } = DefaultTimeZone;

    public const string DefaultTimeZone = "Asia/Riyadh";

    /// <summary>The zones S32 offers: the region's, plus UTC.</summary>
    public static readonly IReadOnlyList<string> TimeZones =
        ["Asia/Riyadh", "Asia/Dubai", "Asia/Kuwait", "Asia/Qatar", "Asia/Bahrain", "Asia/Muscat", "Asia/Amman", "Africa/Cairo", "Europe/London", "UTC"];

    public void SetTimeZone(string timeZone)
    {
        if (!TimeZones.Contains(timeZone) || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out _))
        {
            throw new DomainException(ErrorCodes.InvalidTimeZone, $"Unknown time zone '{timeZone}'.", timeZone ?? string.Empty);
        }

        TimeZone = timeZone;
    }

    public static StaffProfile Create(Guid userId, string displayNameAr, string displayNameEn, byte? onCallTier = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayNameAr);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayNameEn);
        return new StaffProfile(userId, displayNameAr.Trim(), displayNameEn.Trim()) { OnCallTier = onCallTier };
    }

    public string DisplayName(string language) => language == "en" ? DisplayNameEn : DisplayNameAr;

    /// <summary>S21: the name patients see, in both languages.</summary>
    public void Rename(string displayNameAr, string displayNameEn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayNameAr);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayNameEn);
        DisplayNameAr = displayNameAr.Trim();
        DisplayNameEn = displayNameEn.Trim();
    }

    public void SetOnCallTier(byte? tier) => OnCallTier = tier;
}
