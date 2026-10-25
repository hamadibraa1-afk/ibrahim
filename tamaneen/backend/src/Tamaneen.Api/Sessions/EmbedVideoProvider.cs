using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Tamaneen.Api.Sessions;

/// <summary>
/// Skeleton adapter for a real video service of the common "embed URL + signed room token" kind (for example a
/// self-hosted server that accepts an HS256 JWT per room). The guide names no provider (H-03, D-v2-1), so this is
/// generic and configured by <c>Video:Embed:*</c>. Rooms need no API call: the room name is the appointment's
/// unguessable room key, and the token admits one participant to that room only until the window closes.
/// A provider with a room-creation API would call it in <see cref="EnsureRoomAsync"/>.
/// </summary>
public sealed class EmbedVideoProvider : IVideoSessionProvider
{
    public const string ProviderName = "embed";
    private readonly EmbedVideoOptions _options;
    private readonly SymmetricSecurityKey _key;
    private readonly JsonWebTokenHandler _handler = new();

    public EmbedVideoProvider(IOptions<VideoOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value.Embed;
        if (!_options.UrlTemplate.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || !_options.UrlTemplate.Contains("{room}", StringComparison.Ordinal)
            || !_options.UrlTemplate.Contains("{token}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Video:Embed:UrlTemplate must be an https URL containing {room} and {token}.");
        }

        var key = Convert.TryFromBase64String(_options.SigningKeyBase64 ?? string.Empty, new byte[512], out var written) ? Convert.FromBase64String(_options.SigningKeyBase64!) : [];
        if (written < 32)
        {
            throw new InvalidOperationException("Video:Embed:SigningKeyBase64 must be at least 32 bytes of base64.");
        }

        _key = new SymmetricSecurityKey(key);
    }

    public string Name => ProviderName;

    public Task<VideoRoom> EnsureRoomAsync(VideoRoomRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(new VideoRoom(ProviderName, request.RoomKey));
    }

    public Task<VideoJoin> IssueJoinAsync(VideoRoom room, VideoParticipant participant, DateTimeOffset expiresAtUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(participant);
        var now = DateTime.UtcNow;
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now.AddSeconds(-30),
            Expires = expiresAtUtc.UtcDateTime,
            Subject = new ClaimsIdentity([new Claim("sub", participant.UserId.ToString("N")), new Claim("room", room.RoomName), new Claim("moderator", participant.Role == "therapist" ? "true" : "false")]),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256),
        });
        var url = _options.UrlTemplate
            .Replace("{room}", Uri.EscapeDataString(room.RoomName), StringComparison.Ordinal)
            .Replace("{token}", Uri.EscapeDataString(token), StringComparison.Ordinal);
        return Task.FromResult(new VideoJoin("embed", url, expiresAtUtc));
    }
}
