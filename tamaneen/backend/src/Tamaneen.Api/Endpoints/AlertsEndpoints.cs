using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.RegularExpressions;
using Tamaneen.Clinical.Scheduling;
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
public static partial class AlertsEndpoints
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

    /// <summary>
    /// One row of the 30-day history, by kind: <c>crisis</c> (a closed crisis event; final = its close outcome),
    /// <c>risk_raised</c> (SB-17; detail from→to, by whom; final = "raised") or <c>no_show</c> (detail
    /// <c>repeated</c>, <c>high_risk</c> or <c>missed</c>; final = <c>rebooked</c> or <c>not_rebooked</c>).
    /// Ids, levels and codes only: never the reason a clinician typed.
    /// </summary>
    public sealed record AlertHistoryDto(string Kind, string Id, string PseudonymId, int Level, DateTimeOffset At, string Detail, IReadOnlyDictionary<string, string> Params, string Final, string? By, Guid? EventId);

    public sealed record AlertsDto(IReadOnlyList<AlertDto> Items, int OpenCount, int HandledCount, IReadOnlyList<AlertHistoryDto> History);

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

        var history = await HistoryAsync(db, user, handled, since, clock, ct).ConfigureAwait(false);
        var people = shown.Select(e => e.AcknowledgedByUserId).Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var names = await db.StaffProfiles.AsNoTracking().Where(s => people.Contains(s.UserId)).ToDictionaryAsync(s => s.UserId, s => s.DisplayName(user.Language), ct).ConfigureAwait(false);
        audit.Append("staff.alerts_viewed", "CrisisEvent", null, null, $"status={status ?? "all"};count={shown.Count};history={history.Count}");
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
            history.Count,
            history));
    }

    /// <summary>
    /// The last 30 days, newest first: closed crisis events, risk raises (SB-17, from the audit ledger) and missed
    /// sessions. Patients are the ones row-level security shows the reader (PatientProfiles), and for a therapist
    /// only those they actively care for, the same scope as the open alerts.
    /// </summary>
    private static async Task<List<AlertHistoryDto>> HistoryAsync(TamaneenDbContext db, ICurrentUser user, List<CrisisEvent> closed, DateTimeOffset since, IClock clock, CancellationToken ct)
    {
        var visible = db.PatientProfiles.Select(p => p.PatientId);
        if (user.Role == AppRoles.Therapist)
        {
            var me = user.UserId!.Value;
            visible = visible.Where(id => db.CareAssignments.Any(a => a.PatientId == id && a.TherapistUserId == me && a.EndedAtUtc == null));
        }

        var patients = await db.UserAccounts.AsNoTracking().Where(a => visible.Contains(a.Id)).Select(a => new { a.Id, a.PseudonymId }).ToListAsync(ct).ConfigureAwait(false);
        var pseudonyms = patients.Select(p => p.PseudonymId).ToList();
        var ids = patients.Select(p => p.Id).ToList();
        var pseudonymOf = patients.ToDictionary(p => p.Id, p => p.PseudonymId);

        var raises = await db.AuditEntries.AsNoTracking()
            .Where(e => e.Action == "patient.risk_raised" && e.OccurredAtUtc >= since && e.PatientPseudonymId != null && pseudonyms.Contains(e.PatientPseudonymId))
            .OrderByDescending(e => e.OccurredAtUtc).Take(100)
            .Select(e => new { e.Id, e.OccurredAtUtc, e.ActorUserId, e.PatientPseudonymId, e.Reason }).ToListAsync(ct).ConfigureAwait(false);

        var missed = await db.Appointments.AsNoTracking()
            .Where(a => ids.Contains(a.PatientId) && a.Status == AppointmentStatus.NoShow && a.StartsAtUtc >= since)
            .OrderByDescending(a => a.StartsAtUtc).Take(100).ToListAsync(ct).ConfigureAwait(false);
        var missedPatients = missed.Select(a => a.PatientId).Distinct().ToList();
        var sessions = await db.Appointments.AsNoTracking()
            .Where(a => missedPatients.Contains(a.PatientId))
            .Select(a => new { a.Id, a.PatientId, a.StartsAtUtc, a.Status }).ToListAsync(ct).ConfigureAwait(false);
        var risk = await StaffScheduleEndpoints.RiskAsync(db, missedPatients, clock, ct).ConfigureAwait(false);

        var people = raises.Where(r => r.ActorUserId is not null).Select(r => r.ActorUserId!.Value).Distinct().ToList();
        var names = await db.StaffProfiles.AsNoTracking().Where(s => people.Contains(s.UserId)).ToDictionaryAsync(s => s.UserId, s => s.DisplayName(user.Language), ct).ConfigureAwait(false);

        var rows = new List<AlertHistoryDto>();
        rows.AddRange(closed.Select(e => new AlertHistoryDto(
            "crisis", e.Id.ToString(), e.PseudonymId, (int)e.Level, e.ClosedAtUtc ?? e.OpenedAtUtc, "crisis",
            new Dictionary<string, string> { ["number"] = $"CE-{e.Number}" }, e.CloseOutcome ?? "closed", null, e.Id)));

        foreach (var r in raises)
        {
            var m = RaisePattern().Match(r.Reason ?? string.Empty);
            if (!m.Success)
            {
                continue;
            }

            rows.Add(new AlertHistoryDto(
                "risk_raised", r.Id.ToString(CultureInfo.InvariantCulture), r.PatientPseudonymId!, int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), r.OccurredAtUtc, "risk_raised",
                new Dictionary<string, string> { ["from"] = m.Groups[1].Value, ["to"] = m.Groups[2].Value }, "raised",
                r.ActorUserId is { } actor ? names.GetValueOrDefault(actor) : null, null));
        }

        foreach (var a in missed)
        {
            var earlier = sessions.Where(s => s.PatientId == a.PatientId && s.StartsAtUtc < a.StartsAtUtc && s.Status is AppointmentStatus.Completed or AppointmentStatus.NoShow)
                .OrderByDescending(s => s.StartsAtUtc).FirstOrDefault();
            var level = risk.GetValueOrDefault(a.PatientId, 1);
            var detail = earlier?.Status == AppointmentStatus.NoShow ? "repeated" : level >= 3 ? "high_risk" : "missed";
            var rebooked = sessions.Any(s => s.PatientId == a.PatientId && s.StartsAtUtc > a.StartsAtUtc && s.Status is AppointmentStatus.Confirmed or AppointmentStatus.Completed);
            rows.Add(new AlertHistoryDto(
                "no_show", a.Id.ToString(), pseudonymOf.GetValueOrDefault(a.PatientId, string.Empty), level, a.StartsAtUtc, detail,
                new Dictionary<string, string>(), rebooked ? "rebooked" : "not_rebooked", null, null));
        }

        return rows.OrderByDescending(r => r.At).ToList();
    }

    /// <summary>The ledger line of SB-17 starts "from->to: "; only the two levels are read, never the reason after them.</summary>
    [GeneratedRegex(@"^([1-4])->([1-4]):")]
    private static partial Regex RaisePattern();
}
