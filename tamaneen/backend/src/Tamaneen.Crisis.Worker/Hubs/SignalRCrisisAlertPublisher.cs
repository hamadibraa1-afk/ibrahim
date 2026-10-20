using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.Crisis.Gate;
using Tamaneen.SharedKernel;

namespace Tamaneen.Crisis.Worker.Hubs;

/// <summary>Sends one message per language group, each written in that group's language.</summary>
public sealed class SignalRCrisisAlertPublisher : ICrisisAlertPublisher
{
    public const string AlertMethod = "crisisAlert";

    private readonly IHubContext<CrisisHub> _hub;
    private readonly CrisisAlertFactory _factory;
    private readonly TamaneenDbContext _db;

    public SignalRCrisisAlertPublisher(IHubContext<CrisisHub> hub, CrisisAlertFactory factory, TamaneenDbContext db)
    {
        _hub = hub;
        _factory = factory;
        _db = db;
    }

    public Task EventOpenedAsync(CrisisEvent crisisEvent, CancellationToken cancellationToken) => SendAsync(crisisEvent, null, cancellationToken);

    public Task EventUpdatedAsync(CrisisEvent crisisEvent, CrisisUpdateKind kind, CancellationToken cancellationToken) => SendAsync(crisisEvent, kind, cancellationToken);

    private async Task SendAsync(CrisisEvent crisisEvent, CrisisUpdateKind? kind, CancellationToken cancellationToken)
    {
        // The patient's own therapists see the alert on their dashboard even when they are not on call.
        var therapists = await _db.CareAssignments.AsNoTracking().Where(a => a.PatientId == crisisEvent.PatientId && a.EndedAtUtc == null)
            .Select(a => a.TherapistUserId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var acknowledgedBy = crisisEvent.AcknowledgedByUserId is { } ackId
            ? await _db.StaffProfiles.AsNoTracking().Where(p => p.UserId == ackId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;
        foreach (var language in SupportedLanguages.All)
        {
            var message = _factory.Create(crisisEvent, kind, language, acknowledgedBy?.DisplayName(language));
            var groups = therapists.Select(t => CrisisHub.TherapistGroup(t, language)).Prepend(CrisisHub.GroupFor(language)).ToList();
            await _hub.Clients.Groups(groups).SendAsync(AlertMethod, message, cancellationToken).ConfigureAwait(false);
        }
    }
}
