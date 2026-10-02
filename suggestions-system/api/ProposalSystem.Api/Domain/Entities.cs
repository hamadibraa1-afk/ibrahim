namespace ProposalSystem.Api.Domain;

public enum UserRole { Employee, Screener, CommitteeMember, Admin }

public enum UserStatus { Active, Suspended }

public enum ProposalStatus
{
    Submitted,
    UnderScreening,
    ReturnedForEdit,
    WithCommittee,
    PendingExecutiveDecision,
    Accepted,
    Rejected,
}

public enum ProposalClassification { Excellent, VeryGood, Good, Acceptable, NotApplicable }

public enum NotApplicableReason { NotFeasible, OutOfScope, ViolatesPolicy }

public enum ExecutiveDecisionValue { Accepted, Rejected }

public enum FormFieldType { Text, TextArea, Checkbox, Select, Number, Date }

/// <summary>Post-implementation impact measurement lifecycle (3 to 6 months after approval).</summary>
public enum ImpactAssessmentStatus
{
    /// <summary>Approved; the measurement window has not opened yet.</summary>
    Scheduled,
    /// <summary>Window is open; waiting for the implementation owner to measure.</summary>
    Open,
    /// <summary>Measurement submitted; waiting for the administrator to verify it.</summary>
    Submitted,
    /// <summary>Verified and counted in the ROI reports.</summary>
    Verified,
}

/// <summary>
/// The internal user directory. Accounts, roles and the reporting line (ManagerId) all live here;
/// there is no Active Directory dependency. The reporting line drives SLA auto-escalation.
/// </summary>
public class User
{
    public int Id { get; set; }
    public string UserCode { get; set; } = "";
    public string ArabicName { get; set; } = "";
    public string EnglishName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string Department { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Employee;
    public UserStatus Status { get; set; } = UserStatus.Active;
    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    /// <summary>Direct line manager — the first escalation target when this user misses an SLA.</summary>
    public int? ManagerId { get; set; }
    public User? Manager { get; set; }

    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndAt { get; set; }
}

/// <summary>
/// Server-side session. The browser only holds the random token in an HttpOnly cookie; the
/// database keeps its SHA-256 hash, so a leaked database cannot be replayed as live sessions.
/// </summary>
/// <summary>
/// An organisational department, maintained by the administrator in Settings. Users pick their
/// department from this list instead of typing it, so reports group by one spelling.
/// User.Department keeps the name (not an id) so historical proposals keep their snapshot.
/// </summary>
public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>
/// A time-limited claim on a background job, so that with several API instances behind a load
/// balancer only one of them runs the SLA monitor at a time.
/// </summary>
public class JobLease
{
    public string Name { get; set; } = "";
    public string Holder { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
}

public class UserSession
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string TokenHash { get; set; } = "";
    /// <summary>Synchronizer token for CSRF protection, echoed by the SPA in X-XSRF-TOKEN.</summary>
    public string CsrfToken { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    /// <summary>Sliding idle expiry (8 hours after the last activity by default).</summary>
    public DateTime ExpiresAt { get; set; }
    /// <summary>Hard cap no amount of activity can extend past.</summary>
    public DateTime AbsoluteExpiresAt { get; set; }
    public string? UserAgent { get; set; }
    public string? IpAddress { get; set; }
}

public class Proposal
{
    public int Id { get; set; }
    public string ProposalCode { get; set; } = "";
    public string Title { get; set; } = "";
    public string ImplementationMechanism { get; set; } = "";
    public string SubmissionReasons { get; set; } = "";
    public string Department { get; set; } = "";

    public int SubmitterId { get; set; }
    public User Submitter { get; set; } = null!;

    public ProposalStatus Status { get; set; }
    public string? ScreenerNotes { get; set; }
    public string? RejectionReason { get; set; }

    public ProposalClassification? Classification { get; set; }
    public NotApplicableReason? NotApplicableReasonValue { get; set; }
    public string? CommitteeStudy { get; set; }
    public string? CommitteeRecommendation { get; set; }

    public ExecutiveDecisionValue? ExecutiveDecision { get; set; }
    public string? ExecutiveDecisionNotes { get; set; }
    public int? ExecutiveDecisionById { get; set; }
    public User? ExecutiveDecisionBy { get; set; }
    public DateTime? ExecutiveDecisionAt { get; set; }

    public int? OwnerId { get; set; }
    public User? Owner { get; set; }

    public DateTime SubmittedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int ResubmitCount { get; set; }
    public DateTime? LastResubmittedAt { get; set; }

    // ---- SLA & escalation ----
    /// <summary>When the proposal entered its current stage; the stage SLA counts from here.</summary>
    public DateTime StageEnteredAt { get; set; }
    public DateTime SlaDueAt { get; set; }
    /// <summary>Last breach reminder, so the monitor doesn't repeat itself within the reminder interval.</summary>
    public DateTime? LastSlaAlertAt { get; set; }
    public DateTime? EscalatedAt { get; set; }
    public string? EscalationNote { get; set; }
    /// <summary>0 = none, 1 = line manager, 2 = executive management. Reset on every stage change.</summary>
    public int EscalationLevel { get; set; }
    /// <summary>The manager the proposal was rerouted to; they may take the stage action themselves.</summary>
    public int? EscalatedToId { get; set; }
    public User? EscalatedTo { get; set; }

    // ---- Blind review ----
    /// <summary>
    /// Set once, at the stage the reveal policy names. Null means reviewers still see an anonymous
    /// proposal — including forever, for proposals rejected before reaching that stage.
    /// </summary>
    public DateTime? IdentityRevealedAt { get; set; }

    public List<ProposalFieldValue> FieldValues { get; set; } = [];
    public List<CommitteeVote> Votes { get; set; } = [];
    public List<Attachment> Attachments { get; set; } = [];
    public ImpactAssessment? Impact { get; set; }
}

public class FormField
{
    public int Id { get; set; }
    public string FieldKey { get; set; } = "";
    public string LabelAr { get; set; } = "";
    public string? LabelEn { get; set; }
    public string? Placeholder { get; set; }
    public FormFieldType FieldType { get; set; }
    /// <summary>SuggestionData | SuggestionDetails | Impact | ImpactMeasurement</summary>
    public string Section { get; set; } = FormSections.SuggestionDetails;
    public bool IsRequired { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Backed by a dedicated column; can be relabelled but not deleted or retyped.</summary>
    public bool IsSystem { get; set; }
    public int SortOrder { get; set; }
    public string OptionsJson { get; set; } = "[]";
}

public static class FormSections
{
    public const string SuggestionData = "SuggestionData";
    public const string SuggestionDetails = "SuggestionDetails";
    public const string Impact = "Impact";
    /// <summary>Post-implementation "actual impact" fields, filled 3–6 months after approval.</summary>
    public const string ImpactMeasurement = "ImpactMeasurement";

    public static readonly string[] All = [SuggestionData, SuggestionDetails, Impact, ImpactMeasurement];
}

public class ProposalFieldValue
{
    public int Id { get; set; }
    public int ProposalId { get; set; }
    public int FieldId { get; set; }
    public FormField Field { get; set; } = null!;
    public string? Value { get; set; }
}

public class CommitteeVote
{
    public int Id { get; set; }
    public int ProposalId { get; set; }
    public int MemberId { get; set; }
    public User Member { get; set; } = null!;
    public bool Signed { get; set; }
    public DateTime? SignedAt { get; set; }
}

public class Attachment
{
    public int Id { get; set; }
    public int ProposalId { get; set; }
    public string FileName { get; set; } = "";
    public string StoredName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }
    public int UploadedById { get; set; }
}

public class AuditLog
{
    public long Id { get; set; }
    public int ProposalId { get; set; }
    public string ProposalCode { get; set; } = "";
    /// <summary>Null for actions taken by the system (SLA monitor).</summary>
    public int? ActorId { get; set; }
    public string ActorName { get; set; } = "";
    public string ActorRole { get; set; } = "";
    public string Action { get; set; } = "";
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Notification
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
    public int? ProposalId { get; set; }
    public Proposal? Proposal { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// "Actual Impact Measurement" for an approved proposal. Core money and volume figures are real
/// columns so ROI reports aggregate them in SQL; anything else the administrator defines in the
/// ImpactMeasurement form section is stored as ProposalFieldValue rows.
/// </summary>
public class ImpactAssessment
{
    public int Id { get; set; }
    public int ProposalId { get; set; }
    public Proposal Proposal { get; set; } = null!;
    public ImpactAssessmentStatus Status { get; set; }

    /// <summary>Approval + 3 months by default: the earliest a measurement is meaningful.</summary>
    public DateTime OpensAt { get; set; }
    /// <summary>Approval + 6 months by default: after this the owner's manager is alerted.</summary>
    public DateTime DueAt { get; set; }
    public DateTime? OpenedNoticeAt { get; set; }
    public DateTime? EscalatedAt { get; set; }

    // Dedicated measurement fields (AED and counts, per year unless stated).
    public decimal? ActualAnnualSavings { get; set; }
    public decimal? ActualAnnualRevenue { get; set; }
    public decimal? ImplementationCost { get; set; }
    public decimal? HoursSavedPerMonth { get; set; }
    public int? BeneficiariesReached { get; set; }
    public decimal? SatisfactionBefore { get; set; }
    public decimal? SatisfactionAfter { get; set; }
    /// <summary>How far the proposal achieved what it set out to, 0–100.</summary>
    public decimal? TargetAchievementPercent { get; set; }
    /// <summary>1–5 institutional impact rating.</summary>
    public int? ImpactRating { get; set; }
    public string? Summary { get; set; }
    public string? EvidenceReference { get; set; }

    public DateTime? MeasuredAt { get; set; }
    public int? MeasuredById { get; set; }
    public User? MeasuredBy { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public int? VerifiedById { get; set; }
    public User? VerifiedBy { get; set; }
    public string? ReviewNotes { get; set; }

    public decimal? TotalAnnualBenefit =>
        ActualAnnualSavings is null && ActualAnnualRevenue is null
            ? null
            : (ActualAnnualSavings ?? 0) + (ActualAnnualRevenue ?? 0);

    /// <summary>First-year ROI in percent: (benefit − cost) / cost. Null when there is no cost to divide by.</summary>
    public decimal? RoiPercent =>
        TotalAnnualBenefit is { } benefit && ImplementationCost is > 0 and { } cost
            ? Math.Round((benefit - cost) / cost * 100m, 1)
            : null;
}
