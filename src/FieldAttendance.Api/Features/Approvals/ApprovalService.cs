using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Api.Features.Notifications;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Approvals;

public sealed record ApprovalStepView(Guid Id, int Order, string Stage, string? ApproverName, string Status,
    DateTimeOffset? DecidedAt, string? Note, bool IsCurrent);

/// <summary>
/// Routes a request through the chain HR configured. Each level resolves to a real person —
/// the section head, the line manager, the department manager, HR — and a level with nobody
/// in the role is skipped rather than blocking the request forever.
/// </summary>
public sealed class ApprovalService(AppDbContext db, NotificationService notifications, IClock clock)
{
    /// <summary>Creates the chain for a submitted request. Returns true when it is already fully approved.</summary>
    public async Task<bool> StartAsync(RequestKind kind, Guid requestId, Guid employeeId, CancellationToken ct)
    {
        var workforce = await db.EmployeeProfiles.AsNoTracking().Where(p => p.UserId == employeeId)
            .Select(p => (Workforce?)p.Workforce).SingleOrDefaultAsync(ct);
        var stages = await StagesAsync(kind, workforce, ct);
        var approvers = await ApproversAsync(employeeId, ct);

        var order = 0;
        foreach (var stage in stages)
        {
            order++;
            var approver = approvers.GetValueOrDefault(stage);
            var step = new ApprovalStep(kind, requestId, employeeId, order, stage, approver);
            if (approver is null) step.Skip("لا يوجد شاغل لهذا المستوى");
            db.ApprovalSteps.Add(step);
        }

        // Every level empty (no manager on record, or the only approver is the requester) used to
        // leave the request pending with nobody to decide it. HR, or failing that the executive,
        // takes it, so a person always decides. NotSelf already keeps the requester out of both.
        if (!stages.Any(s => approvers.GetValueOrDefault(s) is not null))
        {
            var (stage, fallback) = approvers[ApprovalStage.Hr] is { } hr
                ? (ApprovalStage.Hr, (Guid?)hr) : (ApprovalStage.Executive, approvers[ApprovalStage.Executive]);
            if (fallback is not null)
                db.ApprovalSteps.Add(new ApprovalStep(kind, requestId, employeeId, order + 1, stage, fallback));
        }

        await db.SaveChangesAsync(ct);
        await NotifyCurrentApproverAsync(kind, requestId, employeeId, ct);
        return !await db.ApprovalSteps.AnyAsync(s => s.Kind == kind && s.RequestId == requestId
                                                     && s.Status == ApprovalStepStatus.Pending, ct);
    }

    /// <summary>The step waiting for a signature right now, if any.</summary>
    public async Task<ApprovalStep?> CurrentStepAsync(RequestKind kind, Guid requestId, CancellationToken ct) =>
        await db.ApprovalSteps
            .Where(s => s.Kind == kind && s.RequestId == requestId && s.Status == ApprovalStepStatus.Pending)
            .OrderBy(s => s.Order).FirstOrDefaultAsync(ct);

    /// <summary>
    /// Records one decision. Returns Approved once the last level signs, Rejected on a refusal,
    /// and Pending while the request still has levels to travel.
    /// </summary>
    public async Task<RequestStatus> DecideAsync(RequestKind kind, Guid requestId, Guid userId, bool approve,
        string? note, bool isHrOverride, CancellationToken ct)
    {
        var step = await CurrentStepAsync(kind, requestId, ct)
            ?? throw new DomainException("approval.no_step", "There is no step awaiting a decision.");

        if (step.ApproverId is { } expected && expected != userId && !isHrOverride)
            throw new DomainException("approval.not_yours", "This step is with another approver.");

        if (!approve)
        {
            step.Reject(userId, clock.Now, note ?? string.Empty);
            foreach (var later in await db.ApprovalSteps
                         .Where(s => s.Kind == kind && s.RequestId == requestId && s.Order > step.Order && s.Status == ApprovalStepStatus.Pending)
                         .ToListAsync(ct))
                later.Skip("توقف المسار بالرفض");
            await db.SaveChangesAsync(ct);
            await NotifyEmployeeAsync(kind, requestId, step.EmployeeId, approved: false, ct);
            return RequestStatus.Rejected;
        }

        step.Approve(userId, clock.Now, note);
        await db.SaveChangesAsync(ct);

        var remaining = await CurrentStepAsync(kind, requestId, ct);
        if (remaining is not null) await NotifyCurrentApproverAsync(kind, requestId, step.EmployeeId, ct);
        else await NotifyEmployeeAsync(kind, requestId, step.EmployeeId, approved: true, ct);
        return remaining is null ? RequestStatus.Approved : RequestStatus.Pending;
    }

    public async Task<IReadOnlyList<ApprovalStepView>> TimelineAsync(RequestKind kind, Guid requestId, CancellationToken ct)
    {
        var steps = await db.ApprovalSteps.AsNoTracking()
            .Where(s => s.Kind == kind && s.RequestId == requestId).OrderBy(s => s.Order).ToListAsync(ct);
        var names = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var current = steps.FirstOrDefault(s => s.Status == ApprovalStepStatus.Pending);

        return steps.Select(s => new ApprovalStepView(s.Id, s.Order, s.Stage.ToString(),
            s.ApproverId is { } a ? names.GetValueOrDefault(a) : null, s.Status.ToString(), s.DecidedAt, s.Note,
            current is not null && s.Id == current.Id)).ToList();
    }

    /// <summary>Requests of a kind currently waiting on this user.</summary>
    public async Task<IReadOnlyList<Guid>> WaitingOnAsync(RequestKind kind, Guid userId, bool seeAll, CancellationToken ct)
    {
        var pending = await db.ApprovalSteps.AsNoTracking()
            .Where(s => s.Kind == kind && s.Status == ApprovalStepStatus.Pending).ToListAsync(ct);
        var current = pending.GroupBy(s => s.RequestId)
            .Select(g => g.OrderBy(s => s.Order).First())
            .Where(s => seeAll || s.ApproverId == userId);
        return current.Select(s => s.RequestId).ToList();
    }

    private async Task NotifyCurrentApproverAsync(RequestKind kind, Guid requestId, Guid employeeId, CancellationToken ct)
    {
        var step = await CurrentStepAsync(kind, requestId, ct);
        if (step?.ApproverId is not { } approver) return;

        var name = await db.Users.AsNoTracking().Where(u => u.Id == employeeId).Select(u => u.FullName).SingleOrDefaultAsync(ct);
        var role = await db.Users.AsNoTracking().Where(u => u.Id == approver).Select(u => u.Role).SingleAsync(ct);
        await notifications.RaiseAsync(approver, NotificationKind.RequestAwaitingYou,
            [requestId.ToString("N"), step.Order.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            name, kind.ToString(), InboxFor(role), requestId, ct);
    }

    /// <summary>
    /// The approver's own inbox. Every notification used to open the field module's inbox, which a
    /// department manager or HR reaches only for field staff, and an employee-role approver not at all.
    /// </summary>
    private static string InboxFor(UserRole role) => role switch
    {
        UserRole.Supervisor => "/admin/requests",
        UserRole.SystemAdmin or UserRole.HrManager or UserRole.HrOfficer or UserRole.DepartmentManager => "/hr/requests",
        _ => "/my/approvals",
    };

    private async Task NotifyEmployeeAsync(RequestKind kind, Guid requestId, Guid employeeId, bool approved, CancellationToken ct) =>
        await notifications.RaiseAsync(employeeId, NotificationKind.RequestDecided,
            [requestId.ToString("N"), approved ? "approved" : "rejected"],
            null, approved ? "approved" : "rejected", "/my/requests", requestId, ct);

    /// <summary>
    /// The chain for a kind and workforce: the workforce's own chain, else the general one, else a
    /// single HR level when none is configured yet.
    /// </summary>
    private async Task<IReadOnlyList<ApprovalStage>> StagesAsync(RequestKind kind, Workforce? workforce, CancellationToken ct)
    {
        var flows = await db.ApprovalFlows.AsNoTracking().Include(f => f.Levels)
            .Where(f => f.Kind == kind && f.IsActive).ToListAsync(ct);
        var flow = flows.FirstOrDefault(f => workforce is not null && f.Workforce == workforce)
                   ?? flows.FirstOrDefault(f => f.Workforce == null);
        return flow is null || flow.Levels.Count == 0 ? [ApprovalStage.Hr] : flow.Stages();
    }

    /// <summary>
    /// Gives every pending leave and permission that has no chain one now. Requests filed before
    /// permissions went through approval chains, or created outside the services, would otherwise
    /// sit in nobody's inbox. Idempotent; run at startup.
    /// </summary>
    public async Task StartMissingChainsAsync(CancellationToken ct)
    {
        var chained = db.ApprovalSteps.Select(s => s.RequestId);
        var leaves = await db.LeaveRequests.AsNoTracking()
            .Where(l => l.IsActive && l.Status == RequestStatus.Pending && !chained.Contains(l.Id))
            .Select(l => new { l.Id, l.EmployeeId }).ToListAsync(ct);
        var permissions = await db.PermissionRequests.AsNoTracking()
            .Where(p => p.IsActive && p.Status == RequestStatus.Pending && !chained.Contains(p.Id))
            .Select(p => new { p.Id, p.EmployeeId }).ToListAsync(ct);

        foreach (var l in leaves) await StartAsync(RequestKind.Leave, l.Id, l.EmployeeId, ct);
        foreach (var p in permissions) await StartAsync(RequestKind.Permission, p.Id, p.EmployeeId, ct);
    }

    public Task<bool> HasChainAsync(RequestKind kind, Guid requestId, CancellationToken ct) =>
        db.ApprovalSteps.AnyAsync(s => s.Kind == kind && s.RequestId == requestId, ct);

    /// <summary>Who fills each level for this employee.</summary>
    private async Task<Dictionary<ApprovalStage, Guid?>> ApproversAsync(Guid employeeId, CancellationToken ct)
    {
        var profile = await db.EmployeeProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == employeeId, ct);
        var section = profile?.SectionId is { } sectionId
            ? await db.Sections.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sectionId, ct) : null;
        var department = profile is null ? null
            : await db.Departments.AsNoTracking().SingleOrDefaultAsync(d => d.Id == profile.DepartmentId, ct);
        var hr = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Role == UserRole.HrManager).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        var executive = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Role == UserRole.SystemAdmin).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);

        // Nobody approves their own request: that level falls to the one above it.
        Guid? NotSelf(Guid? candidate) => candidate == employeeId ? null : candidate;

        return new Dictionary<ApprovalStage, Guid?>
        {
            [ApprovalStage.SectionHead] = NotSelf(section?.HeadId),
            [ApprovalStage.LineManager] = NotSelf(profile?.ManagerId),
            [ApprovalStage.DepartmentManager] = NotSelf(department?.ManagerId),
            [ApprovalStage.Hr] = NotSelf(hr),
            [ApprovalStage.Executive] = NotSelf(executive),
        };
    }
}
