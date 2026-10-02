using ProposalSystem.Api.Data;
using ProposalSystem.Api.Domain;

namespace ProposalSystem.Api.Workflow;

public static class AuditActions
{
    public const string Created = "Created";
    public const string Edited = "Edited";
    public const string Resubmitted = "Resubmitted";
    public const string Deleted = "Deleted";
    public const string ScreenApproved = "ScreenApproved";
    public const string ScreenReturned = "ScreenReturned";
    public const string ScreenRejected = "ScreenRejected";
    public const string CommitteeStudySaved = "CommitteeStudySaved";
    public const string CommitteeSigned = "CommitteeSigned";
    public const string ExecutiveAccepted = "ExecutiveAccepted";
    public const string ExecutiveRejected = "ExecutiveRejected";
    public const string OwnerAssigned = "OwnerAssigned";
    public const string AttachmentAdded = "AttachmentAdded";
    public const string AttachmentRemoved = "AttachmentRemoved";
    public const string Escalated = "Escalated";
    public const string AutoEscalated = "AutoEscalated";
    public const string IdentityRevealed = "IdentityRevealed";
    public const string ImpactScheduled = "ImpactScheduled";
    public const string ImpactWindowOpened = "ImpactWindowOpened";
    public const string ImpactMeasured = "ImpactMeasured";
    public const string ImpactVerified = "ImpactVerified";
    public const string ImpactReturned = "ImpactReturned";
    public const string CommitteeSeatReleased = "CommitteeSeatReleased";
}

/// <summary>Append-only record of who did what to which proposal.</summary>
public sealed class AuditTrail(AppDbContext db, TimeProvider clock)
{
    public const string SystemActor = "النظام (إجراء تلقائي)";

    public void Record(Proposal p, User? actor, string action, ProposalStatus? from = null, ProposalStatus? to = null, string? notes = null) =>
        db.AuditLogs.Add(new AuditLog
        {
            // Navigation-less on purpose: the row must survive the proposal being deleted.
            ProposalId = p.Id,
            ProposalCode = p.ProposalCode,
            ActorId = actor?.Id,
            ActorName = actor?.ArabicName ?? SystemActor,
            ActorRole = actor?.Role.ToString() ?? "System",
            Action = action,
            FromStatus = from?.ToString(),
            ToStatus = to?.ToString(),
            Notes = notes is { Length: > 2000 } ? notes[..2000] : notes,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        });
}
