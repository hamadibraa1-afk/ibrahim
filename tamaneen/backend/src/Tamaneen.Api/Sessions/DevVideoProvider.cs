namespace Tamaneen.Api.Sessions;

/// <summary>
/// Development and test provider. It needs no external service and no internet: there is no call at all. Each app
/// shows a local «غرفة تجريبية» / "Test room" with its own camera preview, and learns that the other side has joined
/// from the platform's presence heartbeat.
/// </summary>
public sealed class DevVideoProvider : IVideoSessionProvider
{
    public const string ProviderName = "dev";

    public string Name => ProviderName;

    public Task<VideoRoom> EnsureRoomAsync(VideoRoomRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(new VideoRoom(ProviderName, "dev-" + request.RoomKey));
    }

    public Task<VideoJoin> IssueJoinAsync(VideoRoom room, VideoParticipant participant, DateTimeOffset expiresAtUtc, CancellationToken ct) =>
        Task.FromResult(new VideoJoin("test_room", null, expiresAtUtc));
}
