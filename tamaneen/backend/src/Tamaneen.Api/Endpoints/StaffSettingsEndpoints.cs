using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Tamaneen.Api.Auth;
using Tamaneen.Identity;
using Tamaneen.Identity.Auth;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Auth;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Endpoints;

/// <summary>
/// S32 «إعدادات الحساب» for staff, after the patient's P32 and P33 endpoints. Every security change needs the
/// password and a current authenticator code (step-up) and is audited. Two-step verification cannot be turned off
/// for staff, only moved to another device: the new secret takes over once a code from it is checked, and the old
/// device works until then. Risk alerts cannot be switched off, so they are not a setting; the other three
/// switches are kept per person. Language uses PUT /v1/me/language like everyone else.
/// </summary>
public static class StaffSettingsEndpoints
{
    /// <summary>Staff sessions end after 15 minutes without activity (S32 rule), shown on the screen.</summary>
    public const int IdleTimeoutMinutes = 15;

    public static readonly TimeSpan MoveWindow = TimeSpan.FromMinutes(10);
    private const string MovePurpose = "totp-move";

    public sealed record StaffSecurityDto(DateTimeOffset PasswordChangedAt, bool TotpEnabled, int RecoveryCodesRemaining, int IdleTimeoutMinutes, IReadOnlyList<MeEndpoints.SessionDto> Sessions);

    public sealed record StepUpRequest(string? Password, string? Code);

    public sealed record StaffPasswordRequest(string? CurrentPassword, string? NewPassword, string? Code);

    public sealed record TotpMoveDto(string Secret, string OtpauthUri, string MoveToken, DateTimeOffset ExpiresAt);

    public sealed record TotpMoveConfirmRequest(string? MoveToken, string? Code);

    public sealed record StaffNotificationPrefsDto(bool RiskAlerts, bool Bookings, bool WeeklyReport, bool DailyDigest);

    private sealed record MoveTicket(Guid UserId, string Secret, DateTimeOffset ExpiresAt);

    public static IEndpointRouteBuilder MapStaffSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/staff/me").RequireAuthorization(p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Therapist, AppRoles.Supervisor, AppRoles.ClinicalLead, AppRoles.Admin));
        g.MapGet("/security", SecurityAsync);
        g.MapPut("/password", ChangePasswordAsync);
        g.MapPost("/totp/move", StartMoveAsync);
        g.MapPost("/totp/move/confirm", ConfirmMoveAsync);
        g.MapPost("/recovery-codes", RecoveryCodesAsync);
        g.MapDelete("/sessions/{id:guid}", RevokeSessionAsync);
        g.MapPost("/sessions/revoke-others", RevokeOthersAsync);
        g.MapGet("/notifications", GetPrefsAsync);
        g.MapPut("/notifications", UpdatePrefsAsync);
        return app;
    }

    internal static async Task<Ok<StaffSecurityDto>> SecurityAsync(HttpContext http, ICurrentUser user, TamaneenDbContext db, IClock clock, CancellationToken ct)
    {
        var me = user.UserId!.Value;
        var credential = await db.Credentials.AsNoTracking().FirstAsync(c => c.UserId == me, ct).ConfigureAwait(false);
        var remaining = await db.RecoveryCodes.CountAsync(r => r.UserId == me && r.UsedAtUtc == null, ct).ConfigureAwait(false);
        var now = clock.UtcNow;
        var current = MeEndpoints.CurrentSession(http);
        var sessions = await db.AuthSessions.AsNoTracking().Where(s => s.UserId == me && s.RevokedAtUtc == null && s.ExpiresAtUtc > now)
            .OrderByDescending(s => s.LastActiveAtUtc).ToListAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new StaffSecurityDto(
            credential.PasswordChangedAtUtc,
            credential.TotpEnabled,
            remaining,
            IdleTimeoutMinutes,
            sessions.Select(s => new MeEndpoints.SessionDto(s.Id, s.DeviceLabel, s.Id == current, s.Id == current ? now : s.LastActiveAtUtc)).ToList()));
    }

    /// <summary>Current password plus an authenticator code; the other sessions end.</summary>
    internal static async Task<NoContent> ChangePasswordAsync(StaffPasswordRequest request, HttpContext http, ICurrentUser user, TamaneenDbContext db, PasswordService passwords, AuthService auth, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var me = user.UserId!.Value;
        var credential = await StepUpAsync(db, passwords, auth, me, request.CurrentPassword, request.Code, ct).ConfigureAwait(false);
        PasswordService.EnsureStrong(request.NewPassword);
        credential.ChangePassword(passwords.Hash(request.NewPassword!), clock);
        audit.Append("auth.password_changed", "UserAccount", me.ToString());
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await auth.RevokeAllAsync(me, MeEndpoints.CurrentSession(http), ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    /// <summary>«نقل لجهاز آخر», step 1: after the step-up, a new secret for the new device. Nothing changes yet.</summary>
    internal static async Task<Ok<TotpMoveDto>> StartMoveAsync(StepUpRequest request, ICurrentUser user, TamaneenDbContext db, PasswordService passwords, AuthService auth, ServerSecrets secrets, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var me = user.UserId!.Value;
        await StepUpAsync(db, passwords, auth, me, request.Password, request.Code, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false); // the used authenticator step
        var secret = Totp.NewSecret();
        var expires = clock.UtcNow + MoveWindow;
        var ticket = secrets.Protect(MovePurpose, JsonSerializer.Serialize(new MoveTicket(me, Convert.ToBase64String(secret), expires)));
        var base32 = Totp.Base32(secret);
        var pseudonym = await db.UserAccounts.AsNoTracking().Where(a => a.Id == me).Select(a => a.PseudonymId).FirstAsync(ct).ConfigureAwait(false);
        audit.Append("auth.totp_move_started", "UserAccount", me.ToString());
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new TotpMoveDto(base32, Totp.OtpAuthUri(base32, pseudonym), OneTimeCodes.Base64Url(ticket), expires));
    }

    /// <summary>Step 2: a code from the new device; only then does the new secret replace the old one.</summary>
    internal static async Task<NoContent> ConfirmMoveAsync(TotpMoveConfirmRequest request, ICurrentUser user, TamaneenDbContext db, ServerSecrets secrets, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var me = user.UserId!.Value;
        MoveTicket? ticket;
        try
        {
            ticket = JsonSerializer.Deserialize<MoveTicket>(secrets.Unprotect(MovePurpose, FromBase64Url(request.MoveToken ?? string.Empty)));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            ticket = null;
        }

        if (ticket is null || ticket.UserId != me || ticket.ExpiresAt < clock.UtcNow)
        {
            throw new DomainException(ErrorCodes.TotpMoveExpired, "Authenticator move expired or invalid.");
        }

        var secret = Convert.FromBase64String(ticket.Secret);
        var step = Totp.Verify(secret, request.Code, clock.UtcNow) ?? throw new DomainException(ErrorCodes.MfaFailed, "Code from the new device rejected.");
        var credential = await db.Credentials.FirstAsync(c => c.UserId == me, ct).ConfigureAwait(false);
        credential.ReplaceTotp(secrets.Protect(AuthService.TotpPurpose, ticket.Secret), step);
        audit.Append("auth.totp_moved", "UserAccount", me.ToString());
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    internal static async Task<Ok<MeEndpoints.RecoveryCodesDto>> RecoveryCodesAsync(StepUpRequest request, ICurrentUser user, TamaneenDbContext db, PasswordService passwords, AuthService auth, IAuditLog audit, CancellationToken ct)
    {
        var me = user.UserId!.Value;
        await StepUpAsync(db, passwords, auth, me, request.Password, request.Code, ct).ConfigureAwait(false);
        var codes = auth.NewRecoveryCodes(db, me);
        audit.Append("auth.recovery_codes_regenerated", "UserAccount", me.ToString());
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new MeEndpoints.RecoveryCodesDto(codes));
    }

    internal static async Task<NoContent> RevokeSessionAsync(Guid id, ICurrentUser user, TamaneenDbContext db, ISessionCheck sessions, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var session = await db.AuthSessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == user.UserId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Session {id} not found.");
        session.Revoke(clock);
        audit.Append("auth.session_revoked", "AuthSession", id.ToString());
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        sessions.Forget(id);
        return TypedResults.NoContent();
    }

    /// <summary>«إنهاء الأخرى»: every session but this one.</summary>
    internal static async Task<NoContent> RevokeOthersAsync(HttpContext http, ICurrentUser user, TamaneenDbContext db, AuthService auth, IAuditLog audit, CancellationToken ct)
    {
        audit.Append("auth.sessions_revoked", "UserAccount", user.UserId!.Value.ToString(), null, "others");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await auth.RevokeAllAsync(user.UserId!.Value, MeEndpoints.CurrentSession(http), ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    internal static async Task<Ok<StaffNotificationPrefsDto>> GetPrefsAsync(ICurrentUser user, TamaneenDbContext db, CancellationToken ct)
    {
        var prefs = await db.NotificationPreferences.AsNoTracking().FirstOrDefaultAsync(n => n.UserId == user.UserId, ct).ConfigureAwait(false)
            ?? NotificationPreferences.Default(user.UserId!.Value);
        return TypedResults.Ok(Map(prefs));
    }

    /// <summary>riskAlerts is ignored: risk alerts stay on.</summary>
    internal static async Task<Ok<StaffNotificationPrefsDto>> UpdatePrefsAsync(StaffNotificationPrefsDto request, ICurrentUser user, TamaneenDbContext db, CancellationToken ct)
    {
        var me = user.UserId!.Value;
        var prefs = await db.NotificationPreferences.FirstOrDefaultAsync(n => n.UserId == me, ct).ConfigureAwait(false);
        if (prefs is null)
        {
            prefs = NotificationPreferences.Default(me);
            db.NotificationPreferences.Add(prefs);
        }

        prefs.UpdateStaff(request.Bookings, request.WeeklyReport, request.DailyDigest);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(Map(prefs));
    }

    private static StaffNotificationPrefsDto Map(NotificationPreferences n) => new(true, n.StaffBookings, n.StaffWeeklyReport, n.StaffDailyDigest);

    /// <summary>Password and a current authenticator code. password_mismatch or mfa_failed, both localized.</summary>
    private static async Task<Credential> StepUpAsync(TamaneenDbContext db, PasswordService passwords, AuthService auth, Guid me, string? password, string? code, CancellationToken ct)
    {
        var credential = await db.Credentials.FirstAsync(c => c.UserId == me, ct).ConfigureAwait(false);
        if (passwords.Verify(credential.PasswordHash, password ?? string.Empty) == PasswordCheck.Failed)
        {
            throw new DomainException(ErrorCodes.PasswordMismatch, "Step-up: password wrong.");
        }

        if (!auth.VerifyTotp(credential, code))
        {
            throw new DomainException(ErrorCodes.MfaFailed, "Step-up: authenticator code rejected.");
        }

        return credential;
    }

    private static byte[] FromBase64Url(string s)
    {
        var b = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b.PadRight(b.Length + ((4 - (b.Length % 4)) % 4), '='));
    }
}
