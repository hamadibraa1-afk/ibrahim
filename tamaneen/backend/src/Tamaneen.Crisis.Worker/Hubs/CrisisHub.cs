using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Tamaneen.Hosting;
using Tamaneen.SharedKernel;

namespace Tamaneen.Crisis.Worker.Hubs;

/// <summary>
/// Live channel of the crisis console (S04). Each connection joins the group of its language, so every
/// responder receives alerts written in the language of their own screen. Actions (acknowledge, close) go
/// through HTTP so they are authorised by row-level security and audited.
/// </summary>
[Authorize(Policy = AuthSetup.CrisisResponderPolicy)]
public sealed class CrisisHub : Hub
{
    public const string Path = "/hubs/crisis";

    public static string GroupFor(string language) => $"responders:{SupportedLanguages.Normalize(language)}";

    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        var requested = http?.Request.Query["lang"].ToString();
        var language = SupportedLanguages.IsSupported(requested) ? requested! : http is null ? SupportedLanguages.Default : LocalizationSetup.RequestLanguage(http);
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(language)).ConfigureAwait(false);
        await base.OnConnectedAsync().ConfigureAwait(false);
    }
}

/// <summary>Messages pushed to the console. Contains the pseudonym, never the patient's words.</summary>
public sealed record CrisisAlertMessage(
    Guid EventId,
    string Kind,
    string PseudonymId,
    int Level,
    string LevelLabel,
    string Title,
    string Body,
    string PatientLanguage,
    string PatientLanguageName,
    int EscalationTier,
    string EscalationTierLabel,
    string Status,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset AckDeadlineUtc,
    IReadOnlyList<string> Categories,
    string Dir);
