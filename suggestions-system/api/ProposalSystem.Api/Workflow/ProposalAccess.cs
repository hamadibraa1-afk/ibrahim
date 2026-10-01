using Microsoft.Extensions.Options;
using ProposalSystem.Api.Common;
using ProposalSystem.Api.Domain;

namespace ProposalSystem.Api.Workflow;

/// <summary>What the viewer may do with a proposal right now. Sent to the SPA so it never guesses.</summary>
public sealed record ProposalActions(
    bool CanEdit,
    bool CanDelete,
    bool CanScreen,
    bool CanStudy,
    bool CanSign,
    bool CanDecide,
    bool CanAssignOwner,
    bool CanEscalate,
    bool CanMeasureImpact,
    bool CanVerifyImpact);

/// <summary>
/// Server-side authorization for every proposal action. Roles come from the internal user
/// directory; on top of the role, nobody may screen, sign or decide their own proposal.
/// </summary>
public sealed class ProposalAccess(IOptions<SlaOptions> sla, TimeProvider clock)
{
    public static readonly ProposalStatus[] ScreeningStatuses = [ProposalStatus.Submitted, ProposalStatus.UnderScreening];
    public static readonly ProposalStatus[] SlaTrackedStatuses =
        [ProposalStatus.Submitted, ProposalStatus.UnderScreening, ProposalStatus.WithCommittee, ProposalStatus.PendingExecutiveDecision];
    public static readonly ProposalStatus[] FinalStatuses = [ProposalStatus.Accepted, ProposalStatus.Rejected];

    public static bool IsReviewer(UserRole role) => role is UserRole.Screener or UserRole.CommitteeMember or UserRole.Admin;

    public bool CanView(Proposal p, Viewer v) =>
        IsReviewer(v.Role) || p.SubmitterId == v.Id || p.OwnerId == v.Id || p.EscalatedToId == v.Id;

    public bool CanEdit(Proposal p, Viewer v) =>
        p.SubmitterId == v.Id && p.Status is ProposalStatus.Submitted or ProposalStatus.ReturnedForEdit;

    public bool CanDelete(Proposal p, Viewer v) => p.SubmitterId == v.Id && p.Status == ProposalStatus.Submitted;

    /// <summary>Screeners and admins — or the manager the overdue proposal was rerouted to.</summary>
    public bool CanScreen(Proposal p, Viewer v) =>
        ScreeningStatuses.Contains(p.Status)
        && p.SubmitterId != v.Id
        && (v.Role is UserRole.Screener or UserRole.Admin || p.EscalatedToId == v.Id);

    public bool CanStudy(Proposal p, Viewer v) =>
        p.Status == ProposalStatus.WithCommittee
        && p.SubmitterId != v.Id
        && v.Role is UserRole.CommitteeMember or UserRole.Admin;

    public bool CanSign(Proposal p, Viewer v) =>
        CanStudy(p, v)
        && !string.IsNullOrWhiteSpace(p.CommitteeRecommendation)
        && p.Votes.Any(x => x.MemberId == v.Id && !x.Signed);

    public bool CanDecide(Proposal p, Viewer v) =>
        p.Status == ProposalStatus.PendingExecutiveDecision && v.IsAdmin && p.SubmitterId != v.Id;

    public bool CanAssignOwner(Proposal p, Viewer v) =>
        v.IsAdmin && p.Status is ProposalStatus.PendingExecutiveDecision or ProposalStatus.Accepted;

    public bool CanEscalate(Proposal p, Viewer v) =>
        v.Role is UserRole.Screener or UserRole.Admin && IsOverdue(p);

    public bool CanMeasureImpact(Proposal p, Viewer v)
    {
        if (p.Impact is not { } impact || impact.Status == ImpactAssessmentStatus.Verified)
            return false;
        if (v.IsAdmin)
            return true; // may record early, e.g. when the owner reports by email
        return p.OwnerId == v.Id && impact.Status is ImpactAssessmentStatus.Open or ImpactAssessmentStatus.Submitted;
    }

    public bool CanVerifyImpact(Proposal p, Viewer v) =>
        v.IsAdmin && p.Impact?.Status == ImpactAssessmentStatus.Submitted;

    public ProposalActions ActionsFor(Proposal p, Viewer v) => new(
        CanEdit(p, v), CanDelete(p, v), CanScreen(p, v), CanStudy(p, v), CanSign(p, v), CanDecide(p, v),
        CanAssignOwner(p, v), CanEscalate(p, v), CanMeasureImpact(p, v), CanVerifyImpact(p, v));

    public bool IsOverdue(Proposal p) =>
        SlaTrackedStatuses.Contains(p.Status) && p.SlaDueAt < clock.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Moves the proposal into a new stage: restarts the stage SLA and clears any escalation of the
    /// previous stage, since that bottleneck is now resolved.
    /// </summary>
    public void EnterStage(Proposal p, ProposalStatus status, DateTime now)
    {
        p.Status = status;
        p.StageEnteredAt = now;
        p.UpdatedAt = now;
        p.SlaDueAt = now.AddHours(StageHours(status));
        p.LastSlaAlertAt = null;
        p.EscalationLevel = 0;
        p.EscalatedToId = null;
    }

    private int StageHours(ProposalStatus status)
    {
        var o = sla.Value;
        return status switch
        {
            ProposalStatus.Submitted or ProposalStatus.UnderScreening => o.ScreeningHours,
            ProposalStatus.WithCommittee => o.CommitteeHours,
            ProposalStatus.PendingExecutiveDecision => o.ExecutiveHours,
            ProposalStatus.ReturnedForEdit => o.ReturnedForEditHours,
            _ => 0, // final: the due date stops meaning anything, the SPA ignores it
        };
    }
}
