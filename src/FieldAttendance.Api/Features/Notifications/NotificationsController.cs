using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Notifications;

public sealed record NotificationDto(Guid Id, string Kind, string? Subject, string? Value, string? Link,
    DateTimeOffset CreatedAt, bool IsUnread);

public sealed record MarkReadRequest(IReadOnlyList<Guid>? Ids);

public sealed record NotificationSettingDto(string Kind, bool InApp, bool Email, bool Sms);

/// <summary>The bell: what is waiting for this person, and what they have already seen.</summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController(AppDbContext db, NotificationService notifications, ICurrentUser me) : ControllerBase
{
    private const int PageSize = 50;

    [HttpGet]
    public async Task<IReadOnlyList<NotificationDto>> List([FromQuery] bool unreadOnly, CancellationToken ct)
    {
        var query = db.Notifications.AsNoTracking().Where(n => n.RecipientId == me.RequiredId);
        if (unreadOnly) query = query.Where(n => n.ReadAt == null);

        return (await query.OrderByDescending(n => n.CreatedAtUtc).Take(PageSize).ToListAsync(ct))
            .Select(n => new NotificationDto(n.Id, n.Kind.ToString(), n.Subject, n.Value, n.Link, n.CreatedAtUtc, n.IsUnread))
            .ToList();
    }

    [HttpGet("count")]
    public async Task<IActionResult> Count(CancellationToken ct) =>
        Ok(new { unread = await db.Notifications.CountAsync(n => n.RecipientId == me.RequiredId && n.ReadAt == null, ct) });

    [HttpPost("read")]
    public async Task<IActionResult> MarkRead(MarkReadRequest r, CancellationToken ct)
    {
        await notifications.MarkReadAsync(me.RequiredId, r.Ids, ct);
        return NoContent();
    }

    /// <summary>Which alerts go out, and through which channel. One row per kind.</summary>
    [HttpGet("settings")]
    [Authorize(Policy = HrPolicies.Read)]
    public async Task<IReadOnlyList<NotificationSettingDto>> Settings(CancellationToken ct)
    {
        var stored = await db.NotificationSettings.AsNoTracking().ToDictionaryAsync(s => s.Kind, ct);
        return Enum.GetValues<NotificationKind>()
            .Select(kind => stored.TryGetValue(kind, out var setting)
                ? new NotificationSettingDto(kind.ToString(), setting.InApp, setting.Email, setting.Sms)
                : new NotificationSettingDto(kind.ToString(), true, false, false))
            .ToList();
    }

    [HttpPut("settings")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> SaveSettings(IReadOnlyList<NotificationSettingDto> settings, CancellationToken ct)
    {
        if (settings is not { Count: > 0 }) throw new DomainException("notification.no_settings", "Nothing to save.");

        var stored = await db.NotificationSettings.ToDictionaryAsync(s => s.Kind, ct);
        foreach (var wanted in settings)
        {
            if (!Enum.TryParse<NotificationKind>(wanted.Kind, true, out var kind)) continue;
            if (stored.TryGetValue(kind, out var setting)) setting.Update(wanted.InApp, wanted.Email, wanted.Sms);
            else db.NotificationSettings.Add(new NotificationSetting(kind, wanted.InApp, wanted.Email, wanted.Sms));
        }

        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
