using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Tamaneen.Api.Notifications;
using Tamaneen.Api.Patients;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Crisis;
using Tamaneen.Hosting;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Endpoints;

/// <summary>
/// P38 «ما بعد الأزمة». After a clinician closes the patient's crisis event, the notification worker leaves a
/// crisis.follow_up notice; while it is unread, home sends the patient here. Everything shown comes from
/// structured data, never from the conversation, the risk level or a diagnosis: who handled the event, whether the
/// treating therapist has been told (the server tells them, crisis.closed), the next appointment, and the close
/// steps the responder recorded for the patient (the follow-up call time and the support person code). While an event
/// is still open the screen shows "we are contacting you now" instead (state open). Each read is audited.
/// </summary>
public static class CrisisFollowUpEndpoints
{
    /// <summary>How long after the close the screen stays reachable from home.</summary>
    public static readonly TimeSpan ShownFor = TimeSpan.FromDays(14);

    public sealed record FollowUpDto(
        string State,
        Guid? EventId,
        DateTimeOffset? ClosedAt,
        string? Responder,
        bool Talked,
        bool TherapistInformed,
        string? TherapistName,
        Guid? NextAppointmentId,
        DateTimeOffset? NextAppointmentAt,
        bool Unread,
        DateTimeOffset? FollowUpCallAt = null,
        string? SupportPerson = null);

    public static IEndpointRouteBuilder MapCrisisFollowUpEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/crisis/follow-up").RequireAuthorization(AuthSetup.PatientPolicy);
        g.MapGet("", FollowUpAsync);
        g.MapPost("/seen", SeenAsync);
        return app;
    }

    internal static async Task<Ok<FollowUpDto>> FollowUpAsync(PatientContext p, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var db = p.Db;
        var pid = p.PatientId;
        var now = clock.UtcNow;
        if (await db.CrisisEvents.AsNoTracking().AnyAsync(e => e.PatientId == pid && e.Status != CrisisEventStatus.Closed, ct).ConfigureAwait(false))
        {
            return TypedResults.Ok(new FollowUpDto("open", null, null, null, false, false, null, null, null, false));
        }

        var since = now - ShownFor;
        var closed = await db.CrisisEvents.AsNoTracking().Where(e => e.PatientId == pid && e.Status == CrisisEventStatus.Closed && e.ClosedAtUtc >= since)
            .OrderByDescending(e => e.ClosedAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (closed is null)
        {
            return TypedResults.Ok(new FollowUpDto("none", null, null, null, false, false, null, null, null, false));
        }

        var responderId = closed.AcknowledgedByUserId ?? closed.ClosedByUserId;
        var therapistId = await db.CareAssignments.AsNoTracking().Where(a => a.PatientId == pid && a.EndedAtUtc == null)
            .OrderByDescending(a => a.StartedAtUtc).Select(a => (Guid?)a.TherapistUserId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var ids = new[] { responderId, therapistId }.Where(i => i is not null).Select(i => i!.Value).ToList();
        var names = await db.StaffProfiles.AsNoTracking().Where(s => ids.Contains(s.UserId)).ToDictionaryAsync(s => s.UserId, s => s.DisplayName(p.Language), ct).ConfigureAwait(false);
        var next = await db.Appointments.AsNoTracking().Where(a => a.PatientId == pid && a.Status == AppointmentStatus.Confirmed && a.StartsAtUtc > now)
            .OrderBy(a => a.StartsAtUtc).Select(a => new { a.Id, a.StartsAtUtc }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var key = NotificationWorker.FollowUpKey(closed.Id);
        var unread = await db.Notifications.AnyAsync(n => n.RecipientUserId == pid && n.DedupeKey == key && n.ReadAtUtc == null, ct).ConfigureAwait(false);

        audit.Append("crisis.follow_up_viewed", "CrisisEvent", closed.Id.ToString(), closed.PseudonymId);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new FollowUpDto(
            "closed",
            closed.Id,
            closed.ClosedAtUtc,
            responderId is { } r ? names.GetValueOrDefault(r) : null,
            closed.CloseOutcome == "safe_contacted",
            therapistId is not null,
            therapistId is { } t ? names.GetValueOrDefault(t) : null,
            next?.Id,
            next?.StartsAtUtc,
            unread,
            closed.FollowUpCallAtUtc,
            closed.SupportPerson));
    }

    /// <summary>«العودة للرئيسية»: the follow-up notices are read, so home stops sending the patient here.</summary>
    internal static async Task<NoContent> SeenAsync(PatientContext p, IClock clock, CancellationToken ct)
    {
        var pid = p.PatientId;
        var now = clock.UtcNow;
        await p.Db.Notifications.Where(n => n.RecipientUserId == pid && n.Kind == NotificationKinds.CrisisFollowUp && n.ReadAtUtc == null)
            .ExecuteUpdateAsync(u => u.SetProperty(n => n.ReadAtUtc, now), ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }
}
