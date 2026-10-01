using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProposalSystem.Api.Domain;

namespace ProposalSystem.Api.Workflow;

public sealed record AttachmentDto(int Id, string FileName, string ContentType, long SizeBytes, DateTime UploadedAt);

public sealed record CommitteeVoteDto(int Id, int MemberId, string MemberName, bool Signed, DateTime? SignedAt);

public sealed record ProposalFieldValueDto(int FieldId, string FieldKey, string LabelAr, string FieldType, string Section, string? Value);

public sealed record ImpactAssessmentDto(
    int Id,
    ImpactAssessmentStatus Status,
    DateTime OpensAt,
    DateTime DueAt,
    bool IsOverdue,
    decimal? ActualAnnualSavings,
    decimal? ActualAnnualRevenue,
    decimal? ImplementationCost,
    decimal? HoursSavedPerMonth,
    int? BeneficiariesReached,
    decimal? SatisfactionBefore,
    decimal? SatisfactionAfter,
    decimal? TargetAchievementPercent,
    int? ImpactRating,
    string? Summary,
    string? EvidenceReference,
    decimal? TotalAnnualBenefit,
    decimal? RoiPercent,
    DateTime? MeasuredAt,
    string? MeasuredByName,
    DateTime? VerifiedAt,
    string? VerifiedByName,
    string? ReviewNotes,
    IReadOnlyList<ProposalFieldValueDto> CustomFields);

public sealed record ProposalDto(
    int Id,
    string ProposalCode,
    string Title,
    string ImplementationMechanism,
    string SubmissionReasons,
    string Department,
    int SubmitterId,
    string SubmitterName,
    bool SubmitterHidden,
    DateTime? IdentityRevealedAt,
    ProposalStatus Status,
    string? ScreenerNotes,
    string? RejectionReason,
    ProposalClassification? Classification,
    NotApplicableReason? NotApplicableReasonValue,
    string? CommitteeStudy,
    string? CommitteeRecommendation,
    ExecutiveDecisionValue? ExecutiveDecision,
    string? ExecutiveDecisionNotes,
    string? ExecutiveDecisionByName,
    DateTime? ExecutiveDecisionAt,
    int? OwnerId,
    string? OwnerName,
    IReadOnlyList<AttachmentDto> Attachments,
    DateTime StageEnteredAt,
    DateTime SlaDueAt,
    DateTime SubmittedAt,
    DateTime UpdatedAt,
    IReadOnlyList<CommitteeVoteDto> CommitteeVotes,
    IReadOnlyList<ProposalFieldValueDto> CustomFields,
    int ResubmitCount,
    DateTime? LastResubmittedAt,
    DateTime? EscalatedAt,
    string? EscalationNote,
    int EscalationLevel,
    int? EscalatedToId,
    string? EscalatedToName,
    ImpactAssessmentDto? Impact,
    ProposalActions Actions);

public static class ProposalQueries
{
    /// <summary>Everything <see cref="ProposalMapper"/> reads.</summary>
    public static IQueryable<Proposal> WithDetails(this IQueryable<Proposal> q) => q
        .Include(p => p.Submitter)
        .Include(p => p.Owner)
        .Include(p => p.EscalatedTo)
        .Include(p => p.ExecutiveDecisionBy)
        .Include(p => p.FieldValues).ThenInclude(v => v.Field)
        .Include(p => p.Votes).ThenInclude(v => v.Member)
        .Include(p => p.Attachments)
        .Include(p => p.Impact).ThenInclude(i => i!.MeasuredBy)
        .Include(p => p.Impact).ThenInclude(i => i!.VerifiedBy)
        .AsSplitQuery();
}

/// <summary>
/// The only place a Proposal becomes JSON, so the blind-review masking cannot be forgotten by
/// a new endpoint: identity is stripped here unless the reveal policy allows this viewer to see it.
/// </summary>
public sealed class ProposalMapper(IdentityRevealPolicy reveal, ProposalAccess access, TimeProvider clock)
{
    public ProposalDto ToDto(Proposal p, Viewer viewer)
    {
        var seesIdentity = reveal.CanSeeIdentity(p, viewer);
        var seesDepartment = reveal.CanSeeDepartment(p, viewer);
        var now = clock.GetUtcNow().UtcDateTime;

        return new ProposalDto(
            p.Id,
            p.ProposalCode,
            p.Title,
            p.ImplementationMechanism,
            p.SubmissionReasons,
            seesDepartment ? p.Department : IdentityRevealPolicy.MaskedDepartment,
            seesIdentity ? p.SubmitterId : 0,
            seesIdentity ? p.Submitter.ArabicName : IdentityRevealPolicy.MaskedName,
            !seesIdentity,
            p.IdentityRevealedAt,
            p.Status,
            p.ScreenerNotes,
            p.RejectionReason,
            p.Classification,
            p.NotApplicableReasonValue,
            p.CommitteeStudy,
            p.CommitteeRecommendation,
            p.ExecutiveDecision,
            p.ExecutiveDecisionNotes,
            p.ExecutiveDecisionBy?.ArabicName,
            p.ExecutiveDecisionAt,
            p.OwnerId,
            p.Owner?.ArabicName,
            p.Attachments.OrderBy(a => a.UploadedAt)
                // File names often carry a person's name, so blind reviewers get neutral ones.
                .Select((a, i) => new AttachmentDto(a.Id, seesIdentity ? a.FileName : $"مرفق-{i + 1}{Path.GetExtension(a.FileName)}", a.ContentType, a.SizeBytes, a.UploadedAt))
                .ToList(),
            p.StageEnteredAt,
            p.SlaDueAt,
            p.SubmittedAt,
            p.UpdatedAt,
            p.Votes.OrderBy(v => v.Member.ArabicName).Select(v => new CommitteeVoteDto(v.Id, v.MemberId, v.Member.ArabicName, v.Signed, v.SignedAt)).ToList(),
            FieldValues(p, section => section != FormSections.ImpactMeasurement),
            p.ResubmitCount,
            p.LastResubmittedAt,
            p.EscalatedAt,
            p.EscalationNote,
            p.EscalationLevel,
            p.EscalatedToId,
            p.EscalatedTo?.ArabicName,
            p.Impact is { } impact ? ToDto(p, impact, now) : null,
            access.ActionsFor(p, viewer));
    }

    private static ImpactAssessmentDto ToDto(Proposal p, ImpactAssessment i, DateTime now) => new(
        i.Id, i.Status, i.OpensAt, i.DueAt,
        i.Status is ImpactAssessmentStatus.Scheduled or ImpactAssessmentStatus.Open && i.DueAt < now,
        i.ActualAnnualSavings, i.ActualAnnualRevenue, i.ImplementationCost, i.HoursSavedPerMonth,
        i.BeneficiariesReached, i.SatisfactionBefore, i.SatisfactionAfter, i.TargetAchievementPercent,
        i.ImpactRating, i.Summary, i.EvidenceReference, i.TotalAnnualBenefit, i.RoiPercent,
        i.MeasuredAt, i.MeasuredBy?.ArabicName, i.VerifiedAt, i.VerifiedBy?.ArabicName, i.ReviewNotes,
        FieldValues(p, section => section == FormSections.ImpactMeasurement));

    private static List<ProposalFieldValueDto> FieldValues(Proposal p, Func<string, bool> sectionFilter) =>
        p.FieldValues
            .Where(v => sectionFilter(v.Field.Section))
            .OrderBy(v => v.Field.SortOrder)
            .Select(v => new ProposalFieldValueDto(v.FieldId, v.Field.FieldKey, v.Field.LabelAr, v.Field.FieldType.ToString(), v.Field.Section, v.Value))
            .ToList();

    public static IReadOnlyList<string> ParseOptions(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }
}
