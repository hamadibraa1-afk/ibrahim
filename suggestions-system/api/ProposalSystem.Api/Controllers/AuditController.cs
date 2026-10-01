using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalSystem.Api.Common;
using ProposalSystem.Api.Data;
using ProposalSystem.Api.Domain;
using ProposalSystem.Api.Security;
using ProposalSystem.Api.Workflow;

namespace ProposalSystem.Api.Controllers;

public sealed record AuditLogDto(
    long Id, int ProposalId, string ProposalCode, string ActorName, string ActorRole, string Action,
    string? FromStatus, string? ToStatus, string? Notes, DateTime CreatedAt);

[ApiController]
[Route("api/audit")]
[Authorize]
public sealed class AuditController(AppDbContext db, CurrentUser me, ProposalAccess access, IdentityRevealPolicy reveal) : ControllerBase
{
    /// <summary>Actions taken by the proposer; on an unrevealed proposal these would name them.</summary>
    private static readonly HashSet<string> NotesNameTheProposer = [AuditActions.AttachmentAdded, AuditActions.AttachmentRemoved];

    private Viewer Viewer => new(me.Id, me.Role);

    [HttpGet("proposal/{proposalId:int}")]
    public async Task<List<AuditLogDto>> ForProposal(int proposalId, CancellationToken ct)
    {
        var p = await db.Proposals.FirstOrDefaultAsync(x => x.Id == proposalId, ct) ?? throw ApiException.NotFound("المقترح غير موجود.");
        if (!access.CanView(p, Viewer))
            throw ApiException.Forbidden();
        var logs = await db.AuditLogs.Where(a => a.ProposalId == proposalId).OrderBy(a => a.CreatedAt).ToListAsync(ct);
        return logs.Select(a => Mask(a, p)).ToList();
    }

    [HttpGet]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<List<AuditLogDto>> All(
        [FromQuery] string? search, [FromQuery] string? action, [FromQuery] int? actorId,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int take = 200, CancellationToken ct = default)
    {
        var q = db.AuditLogs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            // Not by actor name: that would let a blind admin find which proposals a person wrote.
            q = q.Where(a => a.ProposalCode.Contains(term) || (a.Notes != null && a.Notes.Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.Action == action);
        if (actorId is { } actor) q = q.Where(a => a.ActorId == actor);
        if (from is { } f) { var start = f.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); q = q.Where(a => a.CreatedAt >= start); }
        if (to is { } t) { var end = t.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); q = q.Where(a => a.CreatedAt < end); }

        var logs = await q.OrderByDescending(a => a.CreatedAt).Take(Math.Clamp(take, 1, 1000)).ToListAsync(ct);
        var ids = logs.Select(l => l.ProposalId).Distinct().ToList();
        var proposals = await db.Proposals.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var result = logs.Select(a => proposals.TryGetValue(a.ProposalId, out var p) ? Mask(a, p) : Plain(a)).ToList();
        // Filtering by actor must not identify a proposer through their masked entries either.
        return actorId is null ? result : result.Where(r => r.ActorName != IdentityRevealPolicy.MaskedActor).ToList();
    }

    private AuditLogDto Mask(AuditLog a, Proposal p)
    {
        var byProposer = a.ActorId == p.SubmitterId;
        if (!byProposer || reveal.CanSeeIdentity(p, Viewer))
            return Plain(a);
        return new AuditLogDto(a.Id, a.ProposalId, a.ProposalCode, IdentityRevealPolicy.MaskedActor, nameof(UserRole.Employee),
            a.Action, a.FromStatus, a.ToStatus, NotesNameTheProposer.Contains(a.Action) ? null : a.Notes, a.CreatedAt);
    }

    private static AuditLogDto Plain(AuditLog a) =>
        new(a.Id, a.ProposalId, a.ProposalCode, a.ActorName, a.ActorRole, a.Action, a.FromStatus, a.ToStatus, a.Notes, a.CreatedAt);
}
