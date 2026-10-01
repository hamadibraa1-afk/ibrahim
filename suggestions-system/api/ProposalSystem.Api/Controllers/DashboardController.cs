using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalSystem.Api.Data;
using ProposalSystem.Api.Domain;
using ProposalSystem.Api.Security;
using ProposalSystem.Api.Workflow;

namespace ProposalSystem.Api.Controllers;

public sealed record PendingByStageDto(int Screening, int Committee, int ExecutiveDecision);
public sealed record DepartmentCountDto(string Department, int Count);
public sealed record DashboardStatsDto(
    int TotalProposals,
    double AcceptanceRate,
    double AverageProcessingDays,
    int SlaBreaches,
    int Escalated,
    PendingByStageDto PendingByStage,
    IReadOnlyList<DepartmentCountDto> ByDepartment,
    IReadOnlyList<ProposalDto> RecentDecisions);

[ApiController]
[Route("api/dashboard")]
[Authorize(Roles = nameof(UserRole.Admin))]
public sealed class DashboardController(AppDbContext db, CurrentUser me, ProposalMapper mapper, TimeProvider clock) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<DashboardStatsDto> Stats(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var rows = await db.Proposals
            .Select(p => new { p.Status, p.Department, p.SubmittedAt, p.ExecutiveDecisionAt, p.UpdatedAt, p.SlaDueAt, p.EscalatedAt })
            .ToListAsync(ct);

        var decided = rows.Where(r => ProposalAccess.FinalStatuses.Contains(r.Status)).ToList();
        var accepted = decided.Count(r => r.Status == ProposalStatus.Accepted);
        var active = rows.Where(r => ProposalAccess.SlaTrackedStatuses.Contains(r.Status)).ToList();

        var recent = await db.Proposals
            .Where(p => p.Status == ProposalStatus.Accepted || p.Status == ProposalStatus.Rejected)
            .OrderByDescending(p => p.UpdatedAt)
            .Take(8)
            .WithDetails()
            .ToListAsync(ct);

        var viewer = new Viewer(me.Id, me.Role);
        return new DashboardStatsDto(
            rows.Count,
            decided.Count == 0 ? 0 : Math.Round(accepted * 100.0 / decided.Count, 1),
            decided.Count == 0 ? 0 : Math.Round(decided.Average(r => ((r.ExecutiveDecisionAt ?? r.UpdatedAt) - r.SubmittedAt).TotalDays), 1),
            active.Count(r => r.SlaDueAt < now),
            active.Count(r => r.EscalatedAt != null),
            new PendingByStageDto(
                active.Count(r => ProposalAccess.ScreeningStatuses.Contains(r.Status)),
                active.Count(r => r.Status == ProposalStatus.WithCommittee),
                active.Count(r => r.Status == ProposalStatus.PendingExecutiveDecision)),
            // Department totals are aggregates; no single proposal is identified by them.
            rows.GroupBy(r => r.Department).Select(g => new DepartmentCountDto(g.Key, g.Count())).OrderByDescending(d => d.Count).ToList(),
            recent.Select(p => mapper.ToDto(p, viewer)).ToList());
    }
}
