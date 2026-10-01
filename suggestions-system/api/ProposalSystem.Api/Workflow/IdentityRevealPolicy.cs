using Microsoft.Extensions.Options;
using ProposalSystem.Api.Common;
using ProposalSystem.Api.Domain;

namespace ProposalSystem.Api.Workflow;

/// <summary>Who is looking — enough to decide visibility without a database round trip.</summary>
public readonly record struct Viewer(int Id, UserRole Role)
{
    public bool IsAdmin => Role == UserRole.Admin;
}

/// <summary>
/// Blind screening governance. The rule, in order:
/// <list type="number">
/// <item>The proposer always sees their own name.</item>
/// <item>Once <see cref="Proposal.IdentityRevealedAt"/> is set, everyone allowed to open the proposal sees it.</item>
/// <item>Before that nobody else does — screeners, committee members, escalation managers, and
/// (unless configured) administrators. The idea is judged, not the person.</item>
/// </list>
/// The reveal is a one-way, audited event stamped when the proposal reaches the configured stage
/// (by default: every committee member has signed, i.e. initial approval). A proposal rejected or
/// withdrawn before that point is never revealed, so a screener can never learn whose idea they declined.
/// </summary>
public sealed class IdentityRevealPolicy(IOptions<BlindReviewOptions> options)
{
    public const string MaskedName = "— محجوب (مراجعة معمّاة) —";
    public const string MaskedActor = "مقدّم الطلب (محجوب)";
    public const string MaskedDepartment = "— محجوب —";

    private readonly BlindReviewOptions _options = options.Value;

    public RevealStage Stage => _options.RevealStage;

    /// <summary>The status whose arrival reveals the proposer.</summary>
    public ProposalStatus RevealOnStatus => _options.RevealOnStatus;

    public bool CanSeeIdentity(Proposal p, Viewer viewer) =>
        viewer.Id == p.SubmitterId
        || p.IdentityRevealedAt is not null
        || (viewer.IsAdmin && _options.AdminSeesIdentityBeforeReveal);

    public bool CanSeeDepartment(Proposal p, Viewer viewer) =>
        !DepartmentMaskedFor(viewer) || CanSeeIdentity(p, viewer);

    /// <summary>True when this viewer sees "masked" in place of the department on unrevealed proposals.</summary>
    public bool DepartmentMaskedFor(Viewer viewer) =>
        _options.HideDepartmentBeforeReveal && !(viewer.IsAdmin && _options.AdminSeesIdentityBeforeReveal);

    /// <summary>Searching by proposer name would let a blind reviewer probe identities, so only those exempt may.</summary>
    public bool MaySearchByName(Viewer viewer) => viewer.IsAdmin && _options.AdminSeesIdentityBeforeReveal;

    /// <summary>
    /// Call on every transition. Stamps the reveal the first time the proposal reaches the reveal
    /// stage and returns true so the caller can audit it.
    /// </summary>
    public bool ApplyOnTransition(Proposal p, ProposalStatus newStatus, DateTime now)
    {
        if (p.IdentityRevealedAt is not null || newStatus != RevealOnStatus)
            return false;
        p.IdentityRevealedAt = now;
        return true;
    }

    /// <summary>Human-readable statement of the rule, shown to reviewers next to the masked name.</summary>
    public string Describe() => Stage switch
    {
        RevealStage.CommitteeReferral => "يُكشف اسم مقدّم الطلب تلقائياً عند قبول الفرز وإحالة المقترح إلى لجنة الدراسة.",
        RevealStage.ExecutiveApproval => "يُكشف اسم مقدّم الطلب تلقائياً فقط بعد موافقة الإدارة التنفيذية على المقترح.",
        _ => "يُكشف اسم مقدّم الطلب تلقائياً بعد اكتمال توقيعات لجنة الدراسة (الاعتماد المبدئي) وانتقال المقترح للقرار التنفيذي.",
    };
}
