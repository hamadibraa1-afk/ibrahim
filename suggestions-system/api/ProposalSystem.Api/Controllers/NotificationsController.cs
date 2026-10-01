using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalSystem.Api.Common;
using ProposalSystem.Api.Data;
using ProposalSystem.Api.Realtime;
using ProposalSystem.Api.Security;

namespace ProposalSystem.Api.Controllers;

/// <summary>
/// Notification history and read state. New notifications are pushed over SignalR
/// (<see cref="NotificationHub"/>); these endpoints serve the initial load and resync after a reconnect.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController(AppDbContext db, CurrentUser me, NotificationService notifications) : ControllerBase
{
    [HttpGet]
    public async Task<List<NotificationDto>> List([FromQuery] int take = 30, CancellationToken ct = default) =>
        await db.Notifications
            .Where(n => n.UserId == me.Id)
            .OrderByDescending(n => n.CreatedAt)
            .Take(Math.Clamp(take, 1, 200))
            .Select(n => new NotificationDto(n.Id, n.Type, n.Message, n.ProposalId, n.Proposal != null ? n.Proposal.Title : null, n.IsRead, n.CreatedAt))
            .ToListAsync(ct);

    [HttpGet("unread-count")]
    public async Task<int> UnreadCount(CancellationToken ct) =>
        await db.Notifications.CountAsync(n => n.UserId == me.Id && !n.IsRead, ct);

    [HttpPost("{id:long}/read")]
    public async Task<IActionResult> MarkRead(long id, CancellationToken ct)
    {
        var updated = await db.Notifications.Where(n => n.Id == id && n.UserId == me.Id).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
        if (updated == 0)
            throw ApiException.NotFound("الإشعار غير موجود.");
        // Other open tabs of the same user update their badge too.
        await notifications.PushUnreadCountAsync(me.Id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await db.Notifications.Where(n => n.UserId == me.Id && !n.IsRead).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
        await notifications.PushUnreadCountAsync(me.Id, ct);
        return NoContent();
    }
}
