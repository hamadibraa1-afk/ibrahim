using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FieldAttendance.Api.Auth;

public static class PasswordHasher
{
    public static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 11);
    public static bool Verify(string password, string? hash) => hash is not null && BCrypt.Net.BCrypt.Verify(password, hash);

    public static void EnsureStrong(string password)
    {
        if (password.Length < 8 || !password.Any(char.IsDigit) || !password.Any(char.IsLetter))
            throw new Domain.Common.DomainException("user.weak_password", "Password must be at least 8 characters with a letter and a digit.");
    }
}

public sealed class TokenService(IOptions<JwtOptions> options, IClock clock)
{
    public (string Token, DateTimeOffset ExpiresAt) Issue(User user)
    {
        var o = options.Value;
        var expires = clock.Now.AddHours(o.ExpiryHours);
        // Short, explicit claim names. The JWT handler rewrites ClaimTypes.* URIs on write,
        // so using them here would break role checks on the way back in.
        var claims = new[]
        {
            new Claim(AppClaims.UserId, user.Id.ToString()),
            new Claim(AppClaims.Name, user.FullName),
            new Claim(AppClaims.Role, user.Role.ToString()),
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Key));
        var token = new JwtSecurityToken(o.Issuer, o.Audience, claims, expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

public sealed record LoginRequest([Required] string EmployeeNumber, [Required] string Password);

public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt, Guid UserId, string FullName, string Role, string Language);

public sealed record ChangePasswordRequest([Required] string CurrentPassword, [Required] string NewPassword);

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AppDbContext db, TokenService tokens, ICurrentUser currentUser) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var number = request.EmployeeNumber.Trim();
        var user = await db.Users.SingleOrDefaultAsync(u => u.EmployeeNumber == number, ct);

        // Same error for unknown account, wrong password and disabled account: no account enumeration.
        if (user is null || !user.IsActive || !PasswordHasher.Verify(request.Password, user.PasswordHash))
            return Unauthorized(new ApiError("auth.invalid_credentials", "Invalid employee number or password."));

        var (token, expires) = tokens.Issue(user);
        return new LoginResponse(token, expires, user.Id, user.FullName, user.Role.ToString(), user.PreferredLanguage);
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == currentUser.RequiredId, ct);
        if (!PasswordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            return BadRequest(new ApiError("auth.wrong_password", "Current password is incorrect."));
        PasswordHasher.EnsureStrong(request.NewPassword);
        user.SetPasswordHash(PasswordHasher.Hash(request.NewPassword));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
