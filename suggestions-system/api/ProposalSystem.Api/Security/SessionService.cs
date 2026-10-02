using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProposalSystem.Api.Common;
using ProposalSystem.Api.Data;
using ProposalSystem.Api.Domain;
using ProposalSystem.Api.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace ProposalSystem.Api.Security;

public sealed record IssuedSession(string Token, string CsrfToken, DateTime ExpiresAt);

/// <summary>
/// Opaque server-side sessions carried in an HttpOnly cookie. Revocation is immediate (logout,
/// password change, suspension) because every request is checked against the table.
/// </summary>
public sealed class SessionService(AppDbContext db, IOptions<SessionCookieOptions> options, TimeProvider clock, IHubContext<NotificationHub> hub)
{
    private readonly SessionCookieOptions _options = options.Value;

    public SessionCookieOptions Options => _options;

    public async Task<IssuedSession> CreateAsync(User user, HttpContext http, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var csrf = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var session = new UserSession
        {
            UserId = user.Id,
            TokenHash = HashToken(token),
            CsrfToken = csrf,
            CreatedAt = now,
            LastSeenAt = now,
            AbsoluteExpiresAt = now.AddHours(_options.AbsoluteLifetimeHours),
            UserAgent = Truncate(http.Request.Headers.UserAgent.ToString(), 300),
            IpAddress = http.Connection.RemoteIpAddress?.ToString(),
        };
        session.ExpiresAt = SlidingExpiry(session, now);
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);
        return new IssuedSession(token, csrf, session.ExpiresAt);
    }

    /// <summary>Finds a live session for the token and slides its expiry forward.</summary>
    public async Task<UserSession?> ValidateAsync(string token, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var hash = HashToken(token);
        var session = await db.Sessions.Include(s => s.User).FirstOrDefaultAsync(s => s.TokenHash == hash, ct);
        if (session is null)
            return null;

        if (session.ExpiresAt <= now || session.AbsoluteExpiresAt <= now || session.User.Status != UserStatus.Active)
        {
            db.Sessions.Remove(session);
            await db.SaveChangesAsync(ct);
            return null;
        }

        if ((now - session.LastSeenAt).TotalSeconds >= _options.TouchIntervalSeconds)
        {
            session.LastSeenAt = now;
            session.ExpiresAt = SlidingExpiry(session, now);
            await db.SaveChangesAsync(ct);
        }
        return session;
    }

    /// <summary>Explicit "keep me signed in" from the SPA; always slides regardless of the touch interval.</summary>
    public async Task<DateTime> ExtendAsync(long sessionId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
                      ?? throw new ApiException(StatusCodes.Status401Unauthorized, "انتهت الجلسة.");
        session.LastSeenAt = now;
        session.ExpiresAt = SlidingExpiry(session, now);
        await db.SaveChangesAsync(ct);
        return session.ExpiresAt;
    }

    public async Task RevokeAsync(long sessionId, CancellationToken ct) =>
        await db.Sessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync(ct);

    /// <summary>
    /// Signs the user out everywhere, optionally keeping the session that asked for it. Open tabs
    /// are told at once over SignalR; each re-checks its own session, so the revoked ones sign
    /// out immediately and a kept session carries on.
    /// </summary>
    public async Task RevokeAllAsync(int userId, long? exceptSessionId, CancellationToken ct)
    {
        var revoked = await db.Sessions.Where(s => s.UserId == userId && s.Id != (exceptSessionId ?? 0)).ExecuteDeleteAsync(ct);
        if (revoked == 0)
            return;
        try
        {
            await hub.Clients.User(userId.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .SendAsync(HubEvents.SessionRevoked, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The sessions are already gone server-side; the next request from a stale tab gets 401.
        }
    }

    public async Task<int> PurgeExpiredAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return await db.Sessions.Where(s => s.ExpiresAt <= now || s.AbsoluteExpiresAt <= now).ExecuteDeleteAsync(ct);
    }

    public void WriteCookies(HttpResponse response, IssuedSession session)
    {
        // No Expires/Max-Age: a browser-session cookie disappears when the browser closes; the
        // server enforces the 8-hour sliding window on its own.
        response.Cookies.Append(_options.CookieName, session.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true,
        });
        WriteCsrfCookie(response, session.CsrfToken);
    }

    public void WriteCsrfCookie(HttpResponse response, string csrfToken) =>
        // Readable by the SPA on purpose (double submit): Angular copies it into X-XSRF-TOKEN.
        response.Cookies.Append(_options.CsrfCookieName, csrfToken, new CookieOptions
        {
            HttpOnly = false,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true,
        });

    public void ClearCookies(HttpResponse response)
    {
        var expired = new CookieOptions { Secure = true, SameSite = SameSiteMode.Strict, Path = "/" };
        response.Cookies.Delete(_options.CookieName, expired);
        response.Cookies.Delete(_options.CsrfCookieName, expired);
    }

    internal static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private DateTime SlidingExpiry(UserSession session, DateTime now)
    {
        var idle = now.AddHours(_options.IdleTimeoutHours);
        return idle < session.AbsoluteExpiresAt ? idle : session.AbsoluteExpiresAt;
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];
}
