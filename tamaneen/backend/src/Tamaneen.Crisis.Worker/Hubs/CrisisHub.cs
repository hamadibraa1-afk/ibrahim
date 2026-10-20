using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Tamaneen.Hosting;
using Microsoft.EntityFrameworkCore;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;

namespace Tamaneen.Crisis.Worker.Hubs;

/// <summary>
/// Live channel of the crisis console (S04) and the therapist dashboard (S01). Supervisors, the clinical lead
/// and on-call staff join the responder group of their language and receive every alert. Other therapists join
/// a group of their own and receive alerts only for patients assigned to them. Actions go through HTTP so
/// they are authorised by row-level security and audited.
/// </summary>
[Authorize(Policy = AuthSetup.CrisisResponderPolicy)]
public sealed class CrisisHub(TamaneenDbContext db) : Hub
{
    public const string Path = "/hubs/crisis";

    public static string GroupFor(string language) => $"responders:{SupportedLanguages.Normalize(language)}";

    public static string TherapistGroup(Guid userId, string language) => $"therapist:{userId:N}:{SupportedLanguages.Normalize(language)}";

    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        var requested = http?.Request.Query["lang"].ToString();
        var language = SupportedLanguages.IsSupported(requested) ? requested! : http is null ? SupportedLanguages.Default : LocalizationSetup.RequestLanguage(http);
        var role = Context.User?.FindFirst(TamaneenClaims.Role)?.Value;
        var userId = Guid.TryParse(Context.User?.FindFirst(TamaneenClaims.Subject)?.Value, out var id) ? id : Guid.Empty;
        var onCall = role == AppRoles.Therapist && await db.StaffProfiles.AsNoTracking().AnyAsync(s => s.UserId == userId && s.OnCallTier != null).ConfigureAwait(false);
        var group = role is AppRoles.Supervisor or AppRoles.ClinicalLead || onCall ? GroupFor(language) : TherapistGroup(userId, language);
        await Groups.AddToGroupAsync(Context.ConnectionId, group).ConfigureAwait(false);
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
    string Dir,
    string Number,
    string Source,
    string SourceLabel,
    string? AcknowledgedBy);
