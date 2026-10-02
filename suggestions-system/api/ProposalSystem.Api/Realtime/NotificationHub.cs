using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ProposalSystem.Api.Domain;
using ProposalSystem.Api.Security;

namespace ProposalSystem.Api.Realtime;

/// <summary>Client method names the Angular app listens for.</summary>
public static class HubEvents
{
    /// <summary>A new notification for this user, with the fresh unread count for the bell badge.</summary>
    public const string NotificationReceived = "notificationReceived";
    /// <summary>The unread count changed without a new notification (marked read in another tab).</summary>
    public const string UnreadCountChanged = "unreadCountChanged";
    /// <summary>A proposal moved through the workflow; open lists and detail pages refresh themselves.</summary>
    public const string ProposalChanged = "proposalChanged";
    /// <summary>Some of this user's sessions were revoked; each tab re-checks its own.</summary>
    public const string SessionRevoked = "sessionRevoked";
}

/// <summary>
/// Server-to-client push channel. Authenticated by the same HttpOnly session cookie as the REST API;
/// the user id comes from the session, never from anything the client sends.
/// </summary>
[Authorize(AuthenticationSchemes = SessionDefaults.Scheme)]
public sealed class NotificationHub : Hub
{
    public static string RoleGroup(UserRole role) => $"role:{role}";

    public override async Task OnConnectedAsync()
    {
        var role = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (Enum.TryParse<UserRole>(role, out var parsed))
            await Groups.AddToGroupAsync(Context.ConnectionId, RoleGroup(parsed));
        await base.OnConnectedAsync();
    }
}
