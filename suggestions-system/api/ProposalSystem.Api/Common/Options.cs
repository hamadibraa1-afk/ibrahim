using ProposalSystem.Api.Domain;

namespace ProposalSystem.Api.Common;

/// <summary>Session cookie settings. Bound from "Security:Session".</summary>
public sealed class SessionCookieOptions
{
    /// <summary>
    /// "__Host-" makes the browser refuse the cookie unless it is Secure, Path=/ and has no Domain,
    /// so a sibling subdomain can never plant or overwrite it.
    /// </summary>
    public string CookieName { get; set; } = "__Host-sci_session";
    public string CsrfCookieName { get; set; } = "XSRF-TOKEN";
    public string CsrfHeaderName { get; set; } = "X-XSRF-TOKEN";
    /// <summary>Idle timeout; every request inside the window pushes the expiry forward again.</summary>
    public int IdleTimeoutHours { get; set; } = 8;
    /// <summary>Absolute lifetime regardless of activity.</summary>
    public int AbsoluteLifetimeHours { get; set; } = 24;
    /// <summary>Don't write LastSeenAt more often than this; keeps sliding cheap.</summary>
    public int TouchIntervalSeconds { get; set; } = 60;
    public int MaxFailedLogins { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}

/// <summary>Bound from "Security:Cors".</summary>
public sealed class CorsSettings
{
    /// <summary>Exact origins only (scheme + host + port). Wildcards are rejected at startup.</summary>
    public string[] AllowedOrigins { get; set; } = [];
    public int PreflightMaxAgeMinutes { get; set; } = 10;
}

/// <summary>Bound from "Security:Hsts".</summary>
public sealed class HstsSettings
{
    public int MaxAgeDays { get; set; } = 365;
    public bool IncludeSubDomains { get; set; } = true;
    /// <summary>Only turn on once every subdomain is HTTPS: preload lists are slow to leave.</summary>
    public bool Preload { get; set; }
}

/// <summary>Where in the workflow reviewers get to see who submitted a proposal.</summary>
public enum RevealStage
{
    /// <summary>When screening approves and the committee receives it.</summary>
    CommitteeReferral,
    /// <summary>When every committee member has signed the recommendation (initial approval).</summary>
    CommitteeApproval,
    /// <summary>Only once the executive decision accepts the proposal.</summary>
    ExecutiveApproval,
}

/// <summary>Bound from "BlindReview".</summary>
public sealed class BlindReviewOptions
{
    public RevealStage RevealStage { get; set; } = RevealStage.CommitteeApproval;
    /// <summary>
    /// Off by default: an administrator can screen too, and the screening must stay blind
    /// whoever does it. Turn on only if your governance requires admins to see names early.
    /// </summary>
    public bool AdminSeesIdentityBeforeReveal { get; set; }
    /// <summary>Department can identify the person in small teams; hide it as well when that matters.</summary>
    public bool HideDepartmentBeforeReveal { get; set; }

    /// <summary>The status whose arrival triggers the reveal.</summary>
    public ProposalStatus RevealOnStatus => RevealStage switch
    {
        RevealStage.CommitteeReferral => ProposalStatus.WithCommittee,
        RevealStage.ExecutiveApproval => ProposalStatus.Accepted,
        _ => ProposalStatus.PendingExecutiveDecision,
    };
}

/// <summary>Bound from "Sla".</summary>
public sealed class SlaOptions
{
    public int CheckIntervalMinutes { get; set; } = 15;
    public int ScreeningHours { get; set; } = 72;
    public int CommitteeHours { get; set; } = 240;
    public int ExecutiveHours { get; set; } = 120;
    /// <summary>Time the employee has to resubmit a returned proposal (shown, not escalated).</summary>
    public int ReturnedForEditHours { get; set; } = 336;
    /// <summary>A breached item is reminded at most once per this many hours.</summary>
    public int ReminderRepeatHours { get; set; } = 24;
    public EscalationOptions Escalation { get; set; } = new();
}

public sealed class EscalationOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>Hours past the deadline before the handlers' line managers are pulled in (level 1).</summary>
    public int Level1AfterHours { get; set; } = 48;
    /// <summary>Hours past the deadline before executive management (all admins) is alerted (level 2).</summary>
    public int Level2AfterHours { get; set; } = 96;
    /// <summary>
    /// When true, a screening-stage proposal is rerouted to the escalation manager, who can then
    /// screen it themselves. Committee and executive stages are only notified: a manager cannot
    /// sign on behalf of a committee member.
    /// </summary>
    public bool RerouteScreeningToManager { get; set; } = true;
    /// <summary>Employee number of the escalation contact used when a handler has no line manager.</summary>
    public string? FallbackUserCode { get; set; }
}

/// <summary>Bound from "ImpactTracking".</summary>
public sealed class ImpactTrackingOptions
{
    public int WindowOpensAfterMonths { get; set; } = 3;
    public int DueAfterMonths { get; set; } = 6;
}

/// <summary>Bound from "Attachments".</summary>
public sealed class AttachmentOptions
{
    public string StoragePath { get; set; } = "App_Data/attachments";
    public int MaxFiles { get; set; } = 10;
    public long MaxFileBytes { get; set; } = 10 * 1024 * 1024;
    public long MaxTotalBytes { get; set; } = 15 * 1024 * 1024;
    public string[] AllowedExtensions { get; set; } =
    [
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".txt", ".csv", ".zip", ".rar", ".7z",
    ];
}
