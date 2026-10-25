using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Tamaneen.Api.Notifications;
using Tamaneen.Hosting;
using Tamaneen.Identity.Notifications;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Endpoints;

/// <summary>
/// The in-app inbox: P46 for patients (/v1/me/inbox) and S41 for staff (/v1/staff/inbox). Row-level security
/// keeps each row to its recipient. The text is written from the catalog in the request language. The staff inbox
/// puts the risk alerts nobody has acknowledged yet on top: they come live from the crisis events the reader may
/// see, are never "read" by opening the inbox, and leave only when someone acknowledges the event.
/// </summary>
public static class NotificationEndpoints
{
    public const int DefaultLimit = 30;
    public const int MaxLimit = 100;

    /// <summary>One inbox entry. <c>pinned</c> marks a live risk alert (staff), which "mark all as read" leaves alone.</summary>
    public sealed record InboxItemDto(string Id, string Kind, string Category, string Text, string? Link, DateTimeOffset CreatedAt, bool Read, bool Pinned, IReadOnlyDictionary<string, string> Data);

    public sealed record InboxDto(IReadOnlyList<InboxItemDto> Items, int UnreadCount, string? NextCursor, IReadOnlyDictionary<string, int> Counts);

    public sealed record UnreadCountDto(int UnreadCount);

    /// <summary>S41 filter buttons: all, risk, appointments, messages, system (everything else).</summary>
    public static readonly IReadOnlySet<string> StaffFilters = new HashSet<string>(StringComparer.Ordinal) { "all", "risk", "appointments", "messages", "system" };

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/v1/me/inbox").RequireAuthorization(AuthSetup.PatientPolicy);
        me.MapGet("", PatientInboxAsync);
        me.MapGet("/unread-count", PatientUnreadAsync);
        me.MapPost("/{id:guid}/read", ReadAsync);
        me.MapPost("/read-all", ReadAllAsync);

        var staff = app.MapGroup("/v1/staff/inbox").RequireAuthorization(p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Therapist, AppRoles.Supervisor, AppRoles.ClinicalLead, AppRoles.Admin));
        staff.MapGet("", StaffInboxAsync);
        staff.MapGet("/unread-count", StaffUnreadAsync);
        staff.MapPost("/{id:guid}/read", ReadAsync);
        staff.MapPost("/read-all", StaffReadAllAsync);
        return app;
    }

    // ---------- patient (P46) ----------

    internal static async Task<Ok<InboxDto>> PatientInboxAsync(string? cursor, int? limit, ICurrentUser user, TamaneenDbContext db, NotificationTexts texts, CancellationToken ct)
    {
        var me = user.UserId!.Value;
        var zone = await db.PatientProfiles.AsNoTracking().Where(p => p.PatientId == me).Select(p => p.TimeZone).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "Asia/Riyadh";
        var (items, next) = await PageAsync(db, me, null, cursor, limit, ct).ConfigureAwait(false);
        var unread = await db.Notifications.CountAsync(n => n.RecipientUserId == me && n.ReadAtUtc == null, ct).ConfigureAwait(false);
        var dtos = await RenderAsync(db, texts, items, user.Language, zone, ct).ConfigureAwait(false);
        return TypedResults.Ok(new InboxDto(dtos, unread, next, new Dictionary<string, int> { ["all"] = unread }));
    }

    internal static async Task<Ok<UnreadCountDto>> PatientUnreadAsync(ICurrentUser user, TamaneenDbContext db, CancellationToken ct) =>
        TypedResults.Ok(new UnreadCountDto(await db.Notifications.CountAsync(n => n.RecipientUserId == user.UserId && n.ReadAtUtc == null, ct).ConfigureAwait(false)));

    /// <summary>Marks one entry read. Another person's entry is invisible under row-level security, so it is a 404.</summary>
    internal static async Task<NoContent> ReadAsync(Guid id, ICurrentUser user, TamaneenDbContext db, IClock clock, CancellationToken ct)
    {
        var row = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.RecipientUserId == user.UserId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Notification {id} not found.");
        row.MarkRead(clock);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    internal static async Task<NoContent> ReadAllAsync(ICurrentUser user, TamaneenDbContext db, IClock clock, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await db.Notifications.Where(n => n.RecipientUserId == user.UserId && n.ReadAtUtc == null)
            .ExecuteUpdateAsync(u => u.SetProperty(n => n.ReadAtUtc, now), ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    // ---------- staff (S41) ----------

    internal static async Task<Ok<InboxDto>> StaffInboxAsync(string? category, string? cursor, int? limit, ICurrentUser user, TamaneenDbContext db, NotificationTexts texts, IStringLocalizer<SharedResource> text, IOptions<PlatformOptions> platform, CancellationToken ct)
    {
        var me = user.UserId!.Value;
        var filter = category is not null && StaffFilters.Contains(category) ? category : "all";
        var zone = platform.Value.TimeZone;
        var alerts = filter is "all" or "risk" && cursor is null ? await RiskAlertsAsync(db, user, text, ct).ConfigureAwait(false) : [];
        var (items, next) = await PageAsync(db, me, filter, cursor, limit, ct).ConfigureAwait(false);
        var dtos = alerts.Concat(await RenderAsync(db, texts, items, user.Language, zone, ct).ConfigureAwait(false)).ToList();

        var unreadByCategory = await db.Notifications.Where(n => n.RecipientUserId == me && n.ReadAtUtc == null)
            .GroupBy(n => n.Category).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct).ConfigureAwait(false);
        int Count(Func<string, bool> match) => unreadByCategory.Where(c => match(c.Key)).Sum(c => c.Count);
        var risk = alerts.Count + Count(c => c == NotificationCatalog.Risk);
        var counts = new Dictionary<string, int>
        {
            ["all"] = alerts.Count + Count(_ => true),
            ["risk"] = risk,
            ["appointments"] = Count(c => c == NotificationCatalog.Appointments),
            ["messages"] = Count(c => c == NotificationCatalog.Messages),
            ["system"] = Count(c => c is not (NotificationCatalog.Risk or NotificationCatalog.Appointments or NotificationCatalog.Messages)),
        };
        return TypedResults.Ok(new InboxDto(dtos, counts["all"], next, counts));
    }

    internal static async Task<Ok<UnreadCountDto>> StaffUnreadAsync(ICurrentUser user, TamaneenDbContext db, IStringLocalizer<SharedResource> text, CancellationToken ct)
    {
        var stored = await db.Notifications.CountAsync(n => n.RecipientUserId == user.UserId && n.ReadAtUtc == null, ct).ConfigureAwait(false);
        var alerts = (await RiskAlertsAsync(db, user, text, ct).ConfigureAwait(false)).Count;
        return TypedResults.Ok(new UnreadCountDto(stored + alerts));
    }

    /// <summary>Marks the stored entries read. Unacknowledged risk alerts are not stored entries: they stay on top until acknowledged.</summary>
    internal static Task<NoContent> StaffReadAllAsync(ICurrentUser user, TamaneenDbContext db, IClock clock, CancellationToken ct) => ReadAllAsync(user, db, clock, ct);

    /// <summary>
    /// Open crisis events nobody has acknowledged, for the reader: a therapist sees their own patients' (active
    /// care assignment), supervisors and the lead every event. Row-level security already limits the rows.
    /// </summary>
    private static async Task<List<InboxItemDto>> RiskAlertsAsync(TamaneenDbContext db, ICurrentUser user, IStringLocalizer<SharedResource> text, CancellationToken ct)
    {
        if (!AppRoles.Clinical.Contains(user.Role ?? string.Empty))
        {
            return [];
        }

        var query = db.CrisisEvents.AsNoTracking().Where(e => e.Status == Tamaneen.Crisis.CrisisEventStatus.Open);
        if (user.Role == AppRoles.Therapist)
        {
            var me = user.UserId!.Value;
            var mine = db.CareAssignments.Where(a => a.TherapistUserId == me && a.EndedAtUtc == null).Select(a => a.PatientId);
            query = query.Where(e => mine.Contains(e.PatientId));
        }

        var events = await query.OrderByDescending(e => e.Level).ThenBy(e => e.OpenedAtUtc).Take(20).ToListAsync(ct).ConfigureAwait(false);
        return events.Select(e => new InboxItemDto(
            e.Id.ToString(),
            "crisis.alert",
            NotificationCatalog.Risk,
            LocalizationSetup.WithLanguage(user.Language, () => text["notify.crisis.alert", text["notify.level." + (int)e.Level].Value, e.PseudonymId].Value),
            "/alerts",
            e.OpenedAtUtc,
            false,
            true,
            new Dictionary<string, string> { ["eventId"] = e.Id.ToString(), ["pseudonymId"] = e.PseudonymId, ["level"] = ((int)e.Level).ToString(CultureInfo.InvariantCulture) })).ToList();
    }

    // ---------- shared ----------

    /// <summary>Newest first. The cursor is the creation time (UTC ticks) of the last entry of the previous page.</summary>
    private static async Task<(List<UserNotification> Items, string? Next)> PageAsync(TamaneenDbContext db, Guid me, string? staffFilter, string? cursor, int? limit, CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var query = db.Notifications.AsNoTracking().Where(n => n.RecipientUserId == me);
        query = staffFilter switch
        {
            "risk" => query.Where(n => n.Category == NotificationCatalog.Risk),
            "appointments" => query.Where(n => n.Category == NotificationCatalog.Appointments),
            "messages" => query.Where(n => n.Category == NotificationCatalog.Messages),
            "system" => query.Where(n => n.Category != NotificationCatalog.Risk && n.Category != NotificationCatalog.Appointments && n.Category != NotificationCatalog.Messages),
            _ => query,
        };
        if (ParseCursor(cursor) is { } c)
        {
            query = query.Where(n => n.CreatedAtUtc < c);
        }

        var rows = await query.OrderByDescending(n => n.CreatedAtUtc).ThenByDescending(n => n.Id).Take(take + 1).ToListAsync(ct).ConfigureAwait(false);
        var next = rows.Count > take ? Cursor(rows[take - 1]) : null;
        return (rows.Take(take).ToList(), next);
    }

    private static string Cursor(UserNotification n) => n.CreatedAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseCursor(string? cursor) =>
        long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) && ticks > 0 && ticks < DateTimeOffset.MaxValue.UtcTicks
            ? new DateTimeOffset(ticks, TimeSpan.Zero)
            : null;

    /// <summary>Writes the text; a therapist named only by id gets their display name in the reader's language.</summary>
    private static async Task<List<InboxItemDto>> RenderAsync(TamaneenDbContext db, NotificationTexts texts, List<UserNotification> rows, string language, string timeZone, CancellationToken ct)
    {
        var data = rows.ToDictionary(r => r.Id, r => JsonSerializer.Deserialize<Dictionary<string, string>>(r.DataJson) ?? []);
        var therapistIds = data.Values.Select(d => d.GetValueOrDefault("therapistId")).Where(v => v is not null).Select(v => Guid.TryParse(v, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).Distinct().ToList();
        var names = therapistIds.Count == 0
            ? []
            : await db.StaffProfiles.AsNoTracking().Where(s => therapistIds.Contains(s.UserId)).ToDictionaryAsync(s => s.UserId, s => s.DisplayName(language), ct).ConfigureAwait(false);
        return rows.Select(r =>
        {
            var values = new Dictionary<string, string>(data[r.Id], StringComparer.Ordinal);
            if (values.TryGetValue("therapistId", out var tid) && Guid.TryParse(tid, out var g) && names.TryGetValue(g, out var name) && !values.ContainsKey("therapistName"))
            {
                values["therapist"] = name;
            }

            return new InboxItemDto(r.Id.ToString(), r.Kind, r.Category, texts.InApp(r.Kind, values, language, timeZone), r.Link, r.CreatedAtUtc, r.IsRead, false, data[r.Id]);
        }).ToList();
    }
}
