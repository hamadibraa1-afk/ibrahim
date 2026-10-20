using System.ComponentModel.DataAnnotations;
using Tamaneen.SharedKernel;

namespace Tamaneen.Crisis;

/// <summary>
/// Response times per level. Values come from configuration (appsettings, later Platform Settings S43 with
/// clinical-lead approval), never from code.
/// </summary>
public sealed class CrisisSlaOptions
{
    public const string Section = "Crisis:Sla";

    [Range(30, 3600)]
    public int CriticalAcknowledgeSeconds { get; set; }

    [Range(60, 7200)]
    public int CriticalContactSeconds { get; set; }

    [Range(60, 7200)]
    public int HighAcknowledgeSeconds { get; set; }

    [Range(60, 14400)]
    public int HighContactSeconds { get; set; }

    [Range(0, 5)]
    public byte MaxEscalationTier { get; set; }

    public TimeSpan AcknowledgeWithin(RiskLevel level) =>
        TimeSpan.FromSeconds(level >= RiskLevel.Critical ? CriticalAcknowledgeSeconds : HighAcknowledgeSeconds);

    public TimeSpan ContactWithin(RiskLevel level) =>
        TimeSpan.FromSeconds(level >= RiskLevel.Critical ? CriticalContactSeconds : HighContactSeconds);
}

/// <summary>Numbers shown in crisis scripts. Admin-managed emergency contacts (S43); the deployment provides the fallback.</summary>
public sealed class CrisisContactOptions
{
    public const string Section = "Crisis:Contacts";

    [Required]
    [RegularExpression(@"^\+?[0-9]{2,15}$")]
    public string EmergencyNumber { get; set; } = string.Empty;

    [Required]
    [RegularExpression(@"^\+?[0-9]{2,15}$")]
    public string SupportLine { get; set; } = string.Empty;
}
