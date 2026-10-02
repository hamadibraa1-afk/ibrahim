using Microsoft.EntityFrameworkCore;
using ProposalSystem.Api.Data;
using ProposalSystem.Api.Domain;
using ProposalSystem.Api.Realtime;

namespace ProposalSystem.Api.Workflow;

/// <summary>
/// The one place that decides when the committee stage is over, so a signature and a member
/// leaving the committee reach the same verdict.
/// </summary>
public sealed class CommitteeProgress(
    AppDbContext db,
    ProposalAccess access,
    IdentityRevealPolicy reveal,
    AuditTrail audit,
    NotificationService notifications)
{
    /// <summary>
    /// Moves the proposal to the executive decision once every remaining seat has signed.
    /// A committee with no seats left never completes on its own; the SLA monitor escalates it.
    /// </summary>
    public async Task<bool> TryCompleteAsync(Proposal p, DateTime now, CancellationToken ct)
    {
        if (p.Status != ProposalStatus.WithCommittee || p.Votes.Count == 0 || !p.Votes.All(v => v.Signed))
            return false;

        access.EnterStage(p, ProposalStatus.PendingExecutiveDecision, now);
        if (reveal.ApplyOnTransition(p, p.Status, now))
            audit.Record(p, null, AuditActions.IdentityRevealed, notes: reveal.Describe());

        var admins = await notifications.UsersInRolesAsync(ct, UserRole.Admin);
        notifications.Notify(admins.Where(a => a != p.SubmitterId), NotificationTypes.CommitteeSigned,
            $"اكتملت توقيعات اللجنة على المقترح {p.ProposalCode} «{p.Title}» وهو بانتظار القرار التنفيذي.", p);
        return true;
    }

    /// <summary>
    /// A suspended member, or one moved out of the committee role, can no longer sign. Their
    /// unsigned seats are withdrawn so open proposals don't wait for them forever. Signatures
    /// already given stay on record. Caller saves and dispatches.
    /// </summary>
    public async Task ReleaseSeatsAsync(User member, User actor, string reason, DateTime now, CancellationToken ct)
    {
        var open = await db.Proposals
            .Include(p => p.Votes)
            .Where(p => p.Status == ProposalStatus.WithCommittee && p.Votes.Any(v => v.MemberId == member.Id && !v.Signed))
            .ToListAsync(ct);

        foreach (var p in open)
        {
            var seat = p.Votes.First(v => v.MemberId == member.Id && !v.Signed);
            p.Votes.Remove(seat);
            db.CommitteeVotes.Remove(seat);
            p.UpdatedAt = now;
            audit.Record(p, actor, AuditActions.CommitteeSeatReleased, notes: $"{member.ArabicName}: {reason}");
            await TryCompleteAsync(p, now, ct);
            notifications.ProposalChanged(p);
        }
    }
}
