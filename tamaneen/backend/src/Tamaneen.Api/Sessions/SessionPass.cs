using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Tamaneen.Infrastructure.Auth;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Sessions;

/// <summary>The holder of a session pass: one person, on one side, of one appointment, until it expires.</summary>
public sealed record SessionPassClaims(Guid AppointmentId, Guid UserId, string Role, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// The platform's own short-lived join token, returned with every join descriptor whatever the provider. The presence
/// heartbeat requires it, so only someone who was let in through the join window can show up as "joined". It is bound
/// to the appointment, the participant and their side, and signed with a key derived from the server key.
/// </summary>
public sealed class SessionPassSigner(ServerSecrets secrets)
{
    private const string Purpose = "video-session-pass";

    public string Issue(SessionPassClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        var payload = string.Join('|', "v1", claims.AppointmentId.ToString("N"), claims.UserId.ToString("N"), claims.Role,
            claims.ExpiresAtUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        var mac = secrets.Mac(Purpose, payload);
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(payload)) + "." + WebEncoders.Base64UrlEncode(mac);
    }

    /// <summary>Throws 403 error.session.token_invalid unless the pass was issued for exactly this appointment, person and side and is still valid.</summary>
    public SessionPassClaims Validate(string? token, Guid appointmentId, Guid userId, string role, DateTimeOffset now)
    {
        var claims = TryRead(token);
        if (claims is null || claims.AppointmentId != appointmentId || claims.UserId != userId || !string.Equals(claims.Role, role, StringComparison.Ordinal) || claims.ExpiresAtUtc < now)
        {
            throw new DomainException(ErrorCodes.SessionTokenInvalid, "Session pass invalid, expired or issued to someone else.");
        }

        return claims;
    }

    private SessionPassClaims? TryRead(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 512 || token.Split('.') is not [var body, var signature])
        {
            return null;
        }

        try
        {
            var payload = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(body));
            if (!ServerSecrets.Equal(secrets.Mac(Purpose, payload), WebEncoders.Base64UrlDecode(signature)))
            {
                return null;
            }

            return payload.Split('|') is ["v1", var appointment, var user, var role, var expires]
                   && Guid.TryParseExact(appointment, "N", out var a) && Guid.TryParseExact(user, "N", out var u)
                   && long.TryParse(expires, NumberStyles.None, CultureInfo.InvariantCulture, out var exp)
                ? new SessionPassClaims(a, u, role, DateTimeOffset.FromUnixTimeSeconds(exp))
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
