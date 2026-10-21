using System.ComponentModel.DataAnnotations;

namespace Tamaneen.Api;

public sealed class PilotCity
{
    [Required]
    public string Code { get; set; } = string.Empty;

    [Required]
    public string NameAr { get; set; } = string.Empty;

    [Required]
    public string NameEn { get; set; } = string.Empty;
}

/// <summary>Public settings of the pilot (P01, P14, P15): numbers to call, the pilot region and service promises.</summary>
public sealed class PlatformOptions
{
    public const string Section = "Platform";

    [Required]
    public string EmergencyNumber { get; set; } = string.Empty;

    [Required]
    public string SupportLine { get; set; } = string.Empty;

    [Range(1, 720)]
    public int MatchWithinHours { get; set; } = 72;

    [Range(1, 168)]
    public int TriageWithinHours { get; set; } = 24;

    [Range(16, 21)]
    public int MinAge { get; set; } = 18;

    [Required]
    public string RegionNameAr { get; set; } = string.Empty;

    [Required]
    public string RegionNameEn { get; set; } = string.Empty;

    [MinLength(1)]
    public List<PilotCity> Cities { get; set; } = [];

    public string TimeZone { get; set; } = "Asia/Riyadh";

    /// <summary>False pauses new sign-ups (EC-26): eligibility and sign-up are refused and the apps show the emergency numbers instead.</summary>
    public bool SignupsOpen { get; set; } = true;

    /// <summary>Base address of the video service; the room id is appended ten minutes before a session.</summary>
    [Required]
    public string VideoBaseUrl { get; set; } = string.Empty;
}
