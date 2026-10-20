using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Tamaneen.Hosting;
using Tamaneen.Identity;
using Tamaneen.Identity.Auth;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Auth;

public sealed record TokenPair(string AccessToken, DateTimeOffset AccessExpiresAt, string RefreshToken, DateTimeOffset RefreshExpiresAt);

/// <summary>
/// Short access tokens (15 minutes, HMAC-SHA256) that carry the session id, so signing out or
/// "sign out everywhere" takes effect at once (see SessionValidator), plus opaque refresh tokens stored
/// as hashes on the session and rotated on every use.
/// </summary>
public sealed class TokenService(IOptions<AuthOptions> options, IClock clock)
{
    public static readonly TimeSpan AccessLifetime = TimeSpan.FromMinutes(15);
    private readonly JsonWebTokenHandler _handler = new();

    public TokenPair Issue(UserAccount account, AuthSession session, string refreshToken)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.SigningKeyBase64))
        {
            throw new InvalidOperationException("Auth:SigningKeyBase64 is required to issue tokens.");
        }

        var now = clock.UtcNow;
        var expires = now + AccessLifetime;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = o.Issuer,
            Audience = o.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(o.SigningKeyBase64)), SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(
            [
                new Claim(TamaneenClaims.Subject, account.Id.ToString()),
                new Claim(TamaneenClaims.Role, account.Role),
                new Claim(TamaneenClaims.Pseudonym, account.PseudonymId),
                new Claim(TamaneenClaims.Session, session.Id.ToString()),
                new Claim("lang", account.PreferredLanguage),
            ]),
        };
        return new TokenPair(_handler.CreateToken(descriptor), expires, refreshToken, session.ExpiresAtUtc);
    }
}
