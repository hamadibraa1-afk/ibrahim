using Microsoft.AspNetCore.SignalR;
using Tamaneen.Crisis.Gate;
using Tamaneen.SharedKernel;

namespace Tamaneen.Crisis.Worker.Hubs;

/// <summary>Sends one message per language group, each written in that group's language.</summary>
public sealed class SignalRCrisisAlertPublisher : ICrisisAlertPublisher
{
    public const string AlertMethod = "crisisAlert";

    private readonly IHubContext<CrisisHub> _hub;
    private readonly CrisisAlertFactory _factory;

    public SignalRCrisisAlertPublisher(IHubContext<CrisisHub> hub, CrisisAlertFactory factory)
    {
        _hub = hub;
        _factory = factory;
    }

    public Task EventOpenedAsync(CrisisEvent crisisEvent, CancellationToken cancellationToken) => SendAsync(crisisEvent, null, cancellationToken);

    public Task EventUpdatedAsync(CrisisEvent crisisEvent, CrisisUpdateKind kind, CancellationToken cancellationToken) => SendAsync(crisisEvent, kind, cancellationToken);

    private async Task SendAsync(CrisisEvent crisisEvent, CrisisUpdateKind? kind, CancellationToken cancellationToken)
    {
        foreach (var language in SupportedLanguages.All)
        {
            var message = _factory.Create(crisisEvent, kind, language);
            await _hub.Clients.Group(CrisisHub.GroupFor(language)).SendAsync(AlertMethod, message, cancellationToken).ConfigureAwait(false);
        }
    }
}
