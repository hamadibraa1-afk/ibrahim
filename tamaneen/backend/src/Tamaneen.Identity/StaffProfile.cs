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

    public static StaffProfile Create(Guid userId, string displayNameAr, string displayNameEn, byte? onCallTier = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayNameAr);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayNameEn);
        return new StaffProfile(userId, displayNameAr.Trim(), displayNameEn.Trim()) { OnCallTier = onCallTier };
    }

    public string DisplayName(string language) => language == "en" ? DisplayNameEn : DisplayNameAr;

    public void SetOnCallTier(byte? tier) => OnCallTier = tier;
}
