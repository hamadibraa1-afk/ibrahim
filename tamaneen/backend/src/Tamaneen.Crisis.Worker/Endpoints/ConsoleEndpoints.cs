using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamaneen.Crisis.Gate;
using Tamaneen.Crisis.Worker.Hubs;
using Tamaneen.Hosting;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;

namespace Tamaneen.Crisis.Worker.Endpoints;

/// <summary>Actions of the crisis console (S04). Run as the signed-in clinician, so row-level security applies.</summary>
public static class ConsoleEndpoints
{
    public sealed record CloseRequest(string? Reason);

    public static IEndpointRouteBuilder MapConsoleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/crisis/events");
        group.MapGet("/open", ListOpenAsync).RequireAuthorization(AuthSetup.CrisisResponderPolicy);
        group.MapPost("/{id:guid}/acknowledge", AcknowledgeAsync).RequireAuthorization(AuthSetup.CrisisResponderPolicy);
        group.MapPost("/{id:guid}/close", CloseAsync).RequireAuthorization(AuthSetup.CrisisCloserPolicy);
        return app;
    }

    internal static async Task<Ok<List<CrisisAlertMessage>>> ListOpenAsync(HttpContext http, TamaneenDbContext db, CrisisAlertFactory factory, CancellationToken cancellationToken)
    {
        var language = LocalizationSetup.RequestLanguage(http);
        var events = await db.CrisisEvents.Where(e => e.Status != CrisisEventStatus.Closed)
            .OrderByDescending(e => e.Level).ThenBy(e => e.AckDeadlineUtc)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(events.Select(e => factory.Create(e, null, language)).ToList());
    }

    internal static async Task<Ok<CrisisAlertMessage>> AcknowledgeAsync(
        Guid id,
        HttpContext http,
        ICurrentUser user,
        TamaneenDbContext db,
        IAuditLog audit,
        ICrisisAlertPublisher alerts,
        CrisisAlertFactory factory,
        IOptions<CrisisSlaOptions> sla,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var evt = await db.CrisisEvents.FirstOrDefaultAsync(e => e.Id == id, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Crisis event {id} is not visible to this user.");
        evt.Acknowledge(user.UserId!.Value, sla.Value.ContactWithin(evt.Level), clock);
        audit.Append("crisis.acknowledged", "CrisisEvent", evt.Id.ToString(), evt.PseudonymId);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two clinicians pressed "take the case" at the same moment: the first one wins.
            throw new DomainException(ErrorCodes.CrisisAlreadyAcknowledged, $"Crisis event {id} was acknowledged concurrently.");
        }

        await alerts.EventUpdatedAsync(evt, CrisisUpdateKind.Acknowledged, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(factory.Create(evt, CrisisUpdateKind.Acknowledged, LocalizationSetup.RequestLanguage(http)));
    }

    internal static async Task<Ok<CrisisAlertMessage>> CloseAsync(
        Guid id,
        CloseRequest request,
        HttpContext http,
        ICurrentUser user,
        TamaneenDbContext db,
        IAuditLog audit,
        ICrisisAlertPublisher alerts,
        CrisisAlertFactory factory,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var evt = await db.CrisisEvents.FirstOrDefaultAsync(e => e.Id == id, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Crisis event {id} is not visible to this user.");
        evt.Close(user.UserId!.Value, user.Role!, request.Reason ?? string.Empty, clock);

        // Closing is the only way a locked chat reopens.
        var locks = await db.ChatLocks.Where(l => l.CrisisEventId == evt.Id && l.ReleasedAtUtc == null).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var chatLock in locks)
        {
            chatLock.Release(user.UserId!.Value, clock);
        }

        audit.Append("crisis.closed", "CrisisEvent", evt.Id.ToString(), evt.PseudonymId, evt.CloseReason);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await alerts.EventUpdatedAsync(evt, CrisisUpdateKind.Closed, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(factory.Create(evt, CrisisUpdateKind.Closed, LocalizationSetup.RequestLanguage(http)));
    }
}
