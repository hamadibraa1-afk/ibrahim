namespace Tamaneen.Api.Sessions;

/// <summary>The appointment a room is for. The room key is the appointment's unguessable RoomId; no names go to a provider.</summary>
public sealed record VideoRoomRequest(Guid AppointmentId, string RoomKey, DateTimeOffset OpensAtUtc, DateTimeOffset ClosesAtUtc);

public sealed record VideoRoom(string Provider, string RoomName);

/// <summary>One person joining: an opaque participant id (never a name or contact) and their side.</summary>
public sealed record VideoParticipant(Guid UserId, string Role);

/// <summary>
/// What a client needs to enter the room. <c>Mode</c> "test_room" means there is no call: the app shows its own
/// local test room. "embed" means the app frames <c>Url</c>, which carries the provider's short-lived token.
/// </summary>
public sealed record VideoJoin(string Mode, string? Url, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// The video service behind a session (P42). No provider has been chosen for the pilot (D-v2-1), so the platform
/// only talks to this interface. A provider creates (or finds) the room for an appointment and issues a join for one
/// participant that expires with the appointment window. Join windows, audit and presence stay in the platform.
/// </summary>
public interface IVideoSessionProvider
{
    /// <summary>"dev" or "embed"; reported to the apps so they know what they are showing.</summary>
    string Name { get; }

    Task<VideoRoom> EnsureRoomAsync(VideoRoomRequest request, CancellationToken ct);

    Task<VideoJoin> IssueJoinAsync(VideoRoom room, VideoParticipant participant, DateTimeOffset expiresAtUtc, CancellationToken ct);
}
