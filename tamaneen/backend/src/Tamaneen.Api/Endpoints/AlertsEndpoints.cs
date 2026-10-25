using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Tamaneen.Crisis;
using Tamaneen.Hosting;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Endpoints;

/// <summary>
/// S20 «التنبيهات»: crisis and risk alerts for the reader's patients, the open ones now and the last 30 days.
/// Built from the crisis events row-level security lets the reader see; a therapist is further limited to the
/// patients they actively care for ("mine"), while supervisors and the lead see all. Acknowledging and handing to
/// the on-call happen on the crisis worker (POST /v1/crisis/events/{id}/acknowledge and /actions kind refer).
/// </summary>
public static class AlertsEndpoints
{
    public static readonly TimeSpan HistoryWindow = TimeSpan.FromDays(30);

    public sealed record AlertDto(
        Guid EventId,
        string Number,
        string PseudonymId,
        int Level,
        string Status,
        string Source,
        IReadOnlyList<string> RuleIds,
        DateTimeOffset OpenedAt,
        DateTimeOffset AckDeadline,
        string? AcknowledgedBy,
        DateTimeOffset? AcknowledgedAt,
        DateTimeOffset? ClosedAt,
        string? CloseOutcome);

    public sealed record AlertsDto(IReadOnlyList<AlertDto> Items, int OpenCount, int HandledCount);

    public static IEndpointRouteBuilder MapAlertsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/staff/alerts", AlertsAsync).RequireAuthorization(AuthSetup.ClinicalPolicy);
        return app;
    }

    /// <summary>status: open (not closed), handled (closed in the last 30 days) or all. Audited.</summary>
    internal static async Task<Ok<AlertsDto>> AlertsAsync(string? status, ICurrentUser user, TamaneenDbContext db, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var since = clock.UtcNow - HistoryWindow;
        var query = db.CrisisEvents.AsNoTracking().Where(e => e.Status != CrisisEventStatus.Closed || e.ClosedAtUtc >= since);
        if (user.Role == AppRoles.Therapist)
        {
            var me = user.UserId!.Value;
            var mine = db.CareAssignments.Where(a => a.TherapistUserId == me && a.EndedAtUtc == null).Select(a => a.PatientId);
            query = query.Where(e => mine.Contains(e.PatientId));
        }

        var events = await query.OrderByDescending(e => e.OpenedAtUtc).Take(200).ToListAsync(ct).ConfigureAwait(false);
        var open = events.Where(e => e.Status != CrisisEventStatus.Closed).OrderByDescending(e => e.Level).ThenBy(e => e.OpenedAtUtc).ToList();
        var handled = events.Where(e => e.Status == CrisisEventStatus.Closed).OrderByDescending(e => e.ClosedAtUtc).ToList();
        var shown = status switch
        {
            "open" => open,
            "handled" => handled,
            _ => [.. open, .. handled],
        };

        var people = shown.Select(e => e.AcknowledgedByUserId).Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var names = await db.StaffProfiles.AsNoTracking().Where(s => people.Contains(s.UserId)).ToDictionaryAsync(s => s.UserId, s => s.DisplayName(user.Language), ct).ConfigureAwait(false);
        audit.Append("staff.alerts_viewed", "CrisisEvent", null, null, $"status={status ?? "all"};count={shown.Count}");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return TypedResults.Ok(new AlertsDto(
            shown.Select(e => new AlertDto(
                e.Id,
                $"CE-{e.Number}",
                e.PseudonymId,
                (int)e.Level,
                e.Status.ToString(),
                e.Source,
                e.TriggerRuleIds.Split(',', StringSplitOptions.RemoveEmptyEntries),
                e.OpenedAtUtc,
                e.AckDeadlineUtc,
                e.AcknowledgedByUserId is { } a ? names.GetValueOrDefault(a) : null,
                e.AcknowledgedAtUtc,
                e.ClosedAtUtc,
                e.CloseOutcome)).ToList(),
            open.Count,
            handled.Count));
    }
}
