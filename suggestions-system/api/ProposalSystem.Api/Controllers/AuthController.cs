using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ProposalSystem.Api.Common;
using ProposalSystem.Api.Data;
using ProposalSystem.Api.Domain;
using ProposalSystem.Api.Security;

namespace ProposalSystem.Api.Controllers;

public sealed record LoginRequest(string UserCode, string Password);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record SessionResponse(DateTime ExpiresAt);
public sealed record LoginResponse(DateTime ExpiresAt, UserDto User);

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AppDbContext db, SessionService sessions, CurrentUser me, TimeProvider clock) : ControllerBase
{
    private const string InvalidCredentials = "الرقم الوظيفي أو كلمة المرور غير صحيحة.";

    /// <summary>
    /// Issues the session as an HttpOnly + Secure + SameSite=Strict cookie. The body carries only
    /// the expiry (for the SPA countdown) and the profile — never the token.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<LoginResponse> Login(LoginRequest req, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var code = (req.UserCode ?? "").Trim();
        var user = await db.Users.Include(u => u.Manager).FirstOrDefaultAsync(u => u.UserCode == code, ct);

        if (user is null)
        {
            // Same work and same answer as a wrong password, so user codes can't be enumerated by timing.
            PasswordHasher.Verify(req.Password ?? "", DummyHash, out _);
            throw new ApiException(StatusCodes.Status401Unauthorized, InvalidCredentials);
        }

        if (user.LockoutEndAt is { } until && until > now)
            throw new ApiException(StatusCodes.Status423Locked,
                $"الحساب مقفل مؤقتاً بسبب محاولات دخول خاطئة متكررة. حاول بعد {Math.Ceiling((until - now).TotalMinutes)} دقيقة.");

        if (!PasswordHasher.Verify(req.Password ?? "", user.PasswordHash, out var needsRehash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= sessions.Options.MaxFailedLogins)
            {
                user.LockoutEndAt = now.AddMinutes(sessions.Options.LockoutMinutes);
                user.FailedLoginCount = 0;
            }
            await db.SaveChangesAsync(ct);
            throw new ApiException(StatusCodes.Status401Unauthorized, InvalidCredentials);
        }

        if (user.Status != UserStatus.Active)
            throw new ApiException(StatusCodes.Status403Forbidden, "الحساب موقوف. تواصل مع مدير النظام.");

        user.FailedLoginCount = 0;
        user.LockoutEndAt = null;
        if (needsRehash)
            user.PasswordHash = PasswordHasher.Hash(req.Password!);
        await db.SaveChangesAsync(ct);

        var session = await sessions.CreateAsync(user, HttpContext, ct);
        sessions.WriteCookies(Response, session);
        return new LoginResponse(session.ExpiresAt, UserDto.From(user));
    }

    /// <summary>Works with or without a live session, so a stale tab can always clean its cookies.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (me.IsAuthenticated)
            await sessions.RevokeAsync(me.SessionId, ct);
        sessions.ClearCookies(Response);
        return NoContent();
    }

    /// <summary>Explicit sliding renewal ("continue working" in the expiry warning).</summary>
    [HttpPost("refresh")]
    [Authorize]
    public async Task<SessionResponse> Refresh(CancellationToken ct) =>
        new(await sessions.ExtendAsync(me.SessionId, ct));

    [HttpGet("me")]
    [Authorize]
    public async Task<UserDto> Me(CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.Manager).FirstAsync(u => u.Id == me.Id, ct);
        // Re-plant the CSRF cookie in case the browser dropped it (e.g. the user cleared one cookie).
        var csrf = User.FindFirst(SessionDefaults.CsrfClaim)?.Value;
        if (csrf is not null)
            sessions.WriteCsrfCookie(Response, csrf);
        return UserDto.From(user);
    }

    /// <summary>Changing the password signs out every other session of this user.</summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<SessionResponse> ChangePassword(ChangePasswordRequest req, CancellationToken ct)
    {
        var user = await db.Users.FirstAsync(u => u.Id == me.Id, ct);
        if (!PasswordHasher.Verify(req.CurrentPassword ?? "", user.PasswordHash, out _))
            throw ApiException.BadRequest("كلمة المرور الحالية غير صحيحة.");
        if (PasswordHasher.ValidatePolicy(req.NewPassword) is { } error)
            throw ApiException.BadRequest(error);
        if (req.NewPassword == req.CurrentPassword)
            throw ApiException.BadRequest("كلمة المرور الجديدة يجب أن تختلف عن الحالية.");

        user.PasswordHash = PasswordHasher.Hash(req.NewPassword);
        await db.SaveChangesAsync(ct);
        await sessions.RevokeAllAsync(user.Id, me.SessionId, ct);
        return new(await sessions.ExtendAsync(me.SessionId, ct));
    }

    private static readonly string DummyHash = PasswordHasher.Hash("dummy-password-for-timing-1");
}
