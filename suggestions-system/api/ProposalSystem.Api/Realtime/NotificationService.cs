using System.Globalization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProposalSystem.Api.Data;
using ProposalSystem.Api.Domain;

namespace ProposalSystem.Api.Realtime;

public static class NotificationTypes
{
    public const string NewSubmission = "NewSubmission";
    public const string ScreeningDecision = "ScreeningDecision";
    public const string AssignedToCommittee = "AssignedToCommittee";
    public const string CommitteeSigned = "CommitteeSigned";
    public const string ExecutiveDecision = "ExecutiveDecision";
    public const string SlaBreach = "SlaBreach";
    public const string OwnerAssigned = "OwnerAssigned";
    public const string Escalation = "Escalation";
    public const string ImpactMeasurementDue = "ImpactMeasurementDue";
    public const string ImpactMeasurementOverdue = "ImpactMeasurementOverdue";
    public const string ImpactSubmitted = "ImpactSubmitted";
    public const string ImpactVerified = "ImpactVerified";
}

public sealed record NotificationDto(long Id, string Type, string Message, int? ProposalId, string? ProposalTitle, bool IsRead, DateTime CreatedAt);

/// <summary>
/// Stores notifications and pushes them over SignalR. Usage is two-phase so a push never
/// announces something that was rolled back: <see cref="Notify"/> stages rows in the same unit of
/// work as the workflow change, and <see cref="DispatchAsync"/> pushes them after SaveChanges.
/// </summary>
public sealed class NotificationService(AppDbContext db, IHubContext<NotificationHub> hub, TimeProvider clock, ILogger<NotificationService> logger)
{
    private readonly List<Notification> _pending = [];
    private readonly List<(int ProposalId, ProposalStatus Status, HashSet<int> Users)> _changes = [];

    public void Notify(IEnumerable<int> userIds, string type, string message, Proposal? proposal)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var userId in userIds.Distinct())
        {
            var n = new Notification
            {
                UserId = userId,
                Type = type,
                Message = message.Length <= 600 ? message : message[..600],
                ProposalId = proposal?.Id is > 0 ? proposal.Id : null,
                Proposal = proposal,
                CreatedAt = now,
            };
            db.Notifications.Add(n);
            _pending.Add(n);
        }
    }

    public void Notify(int userId, string type, string message, Proposal? proposal) => Notify([userId], type, message, proposal);

    /// <summary>
    /// Announces a workflow transition. Reviewers' role groups get it so queues refresh; the
    /// submitter, owner and escalation manager get it personally. The payload carries no identity.
    /// </summary>
    public void ProposalChanged(Proposal proposal)
    {
        var users = new HashSet<int> { proposal.SubmitterId };
        if (proposal.OwnerId is { } owner) users.Add(owner);
        if (proposal.EscalatedToId is { } esc) users.Add(esc);
        _changes.Add((proposal.Id, proposal.Status, users));
    }

    public async Task<IReadOnlyList<int>> UsersInRolesAsync(CancellationToken ct, params UserRole[] roles) =>
        await db.Users.Where(u => roles.Contains(u.Role) && u.Status == UserStatus.Active).Select(u => u.Id).ToListAsync(ct);

    /// <summary>Pushes everything staged since the last call. Call after SaveChanges succeeds.</summary>
    public async Task DispatchAsync(CancellationToken ct)
    {
        var batch = _pending.ToList();
        var changes = _changes.ToList();
        _pending.Clear();
        _changes.Clear();

        try
        {
            if (batch.Count > 0)
            {
                var userIds = batch.Select(n => n.UserId).Distinct().ToList();
                var unread = await db.Notifications
                    .Where(n => userIds.Contains(n.UserId) && !n.IsRead)
                    .GroupBy(n => n.UserId)
                    .Select(g => new { UserId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

                foreach (var n in batch)
                {
                    var dto = new NotificationDto(n.Id, n.Type, n.Message, n.ProposalId, n.Proposal?.Title, n.IsRead, n.CreatedAt);
                    await hub.Clients.User(UserKey(n.UserId)).SendAsync(
                        HubEvents.NotificationReceived, new { notification = dto, unreadCount = unread.GetValueOrDefault(n.UserId) }, ct);
                }
            }

            foreach (var (proposalId, status, users) in changes)
            {
                var payload = new { proposalId, status = status.ToString() };
                await hub.Clients.Groups(
                        NotificationHub.RoleGroup(UserRole.Screener),
                        NotificationHub.RoleGroup(UserRole.CommitteeMember),
                        NotificationHub.RoleGroup(UserRole.Admin))
                    .SendAsync(HubEvents.ProposalChanged, payload, ct);
                await hub.Clients.Users(users.Select(UserKey).ToList()).SendAsync(HubEvents.ProposalChanged, payload, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The notifications are already saved; a failed push only means the badge catches up
            // on the client's next reconnect or page load.
            logger.LogWarning(ex, "Real-time push failed; clients will resync on reconnect.");
        }
    }

    public async Task PushUnreadCountAsync(int userId, CancellationToken ct)
    {
        var count = await db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);
        await hub.Clients.User(UserKey(userId)).SendAsync(HubEvents.UnreadCountChanged, count, ct);
    }

    private static string UserKey(int userId) => userId.ToString(CultureInfo.InvariantCulture);
}
