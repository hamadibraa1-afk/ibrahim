using Microsoft.AspNetCore.Http.HttpResults;
using Tamaneen.Api.Auth;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Endpoints;

/// <summary>
/// Anonymous sign-in endpoints. Rate-limited per client address. The patient BFF moves tokens into
/// HttpOnly cookies; the staff portal keeps the access token in memory and gets the refresh token only as
/// an HttpOnly cookie scoped to /v1/auth.
/// </summary>
public static class AuthEndpoints
{
    public const string RateLimitPolicy = "auth";
    public const string StaffRefreshCookie = "tamaneen_staff_rt";

    public sealed record EligibilityRequest(bool IsAdult, string? CityCode);

    public sealed record EligibilityResponse(bool Eligible, string? EligibilityToken, string? Reason);

    public sealed record NicknameResponse(string Nickname);

    public sealed record SignupRequest(string? EligibilityToken, string? Nickname, string? ContactType, string? Contact, string? Password, string? Language);

    public sealed record ChallengeResponse(Guid ChallengeId, string MaskedContact, int ResendAfterSeconds, int ExpiresInSeconds);

    public sealed record VerifyRequest(Guid ChallengeId, string? Code);

    public sealed record TokenPairDto(string AccessToken, DateTimeOffset AccessExpiresAt, string RefreshToken, DateTimeOffset RefreshExpiresAt);

    public sealed record SignupVerifiedResponse(TokenPairDto Tokens, IReadOnlyList<string> RecoveryCodes);

    public sealed record LoginRequest(string? Contact, string? Password, string? DeviceToken, string? Client);

    public sealed record LoginResponse(string Status, TokenPairDto? Tokens, string? MfaToken, IReadOnlyList<string>? Methods, string? MaskedPhone);

    public sealed record MfaRequest(string? MfaToken, string? Method, string? Code, bool RememberDevice);

    public sealed record MfaSmsRequest(string? MfaToken);

    public sealed record MfaResponse(TokenPairDto Tokens, string? DeviceToken);

    public sealed record RecoverStartRequest(string? Contact);

    public sealed record RecoverCompleteRequest(Guid ChallengeId, string? Code, string? NewPassword);

    public sealed record RecoverWithCodeRequest(string? Contact, string? RecoveryCode, string? NewPassword);

    public sealed record RefreshRequest(string? RefreshToken);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/auth").AllowAnonymous().RequireRateLimiting(RateLimitPolicy);
        g.MapPost("/eligibility", Eligibility);
        g.MapGet("/nickname", (HttpContext http) => TypedResults.Ok(new NicknameResponse(Nicknames.Suggest(Hosting.LocalizationSetup.RequestLanguage(http)))));
        g.MapPost("/signup", SignupAsync);
        g.MapPost("/challenges/{id:guid}/resend", async (Guid id, AuthService auth, CancellationToken ct) => TypedResults.Ok(Map(await auth.ResendAsync(id, ct).ConfigureAwait(false))));
        g.MapPost("/signup/verify", VerifyAsync);
        g.MapPost("/login", LoginAsync);
        g.MapPost("/login/mfa", MfaAsync);
        g.MapPost("/login/mfa/sms", async (MfaSmsRequest request, AuthService auth, CancellationToken ct) =>
        {
            await auth.SendLoginSmsAsync(Required(request.MfaToken, "mfaToken"), ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        });
        g.MapPost("/recover", async (RecoverStartRequest request, AuthService auth, CancellationToken ct) =>
            TypedResults.Accepted((string?)null, Map(await auth.StartRecoveryAsync(Required(request.Contact, "contact"), ct).ConfigureAwait(false))));
        g.MapPost("/recover/complete", async (RecoverCompleteRequest request, AuthService auth, CancellationToken ct) =>
        {
            await auth.CompleteRecoveryAsync(request.ChallengeId, Required(request.Code, "code"), Required(request.NewPassword, "newPassword"), ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        });
        g.MapPost("/recover/code", async (RecoverWithCodeRequest request, AuthService auth, CancellationToken ct) =>
        {
            await auth.RecoverWithCodeAsync(Required(request.Contact, "contact"), Required(request.RecoveryCode, "recoveryCode"), Required(request.NewPassword, "newPassword"), ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        });
        g.MapPost("/refresh", RefreshAsync);
        g.MapPost("/logout", LogoutAsync);
        return app;
    }

    internal static Ok<EligibilityResponse> Eligibility(EligibilityRequest request, AuthService auth)
    {
        var token = auth.IssueEligibility(request.IsAdult, request.CityCode ?? string.Empty, out var reason);
        return TypedResults.Ok(new EligibilityResponse(token is not null, token, reason));
    }

    internal static async Task<Created<ChallengeResponse>> SignupAsync(SignupRequest request, AuthService auth, CancellationToken ct)
    {
        var result = await auth.StartSignupAsync(
            Required(request.EligibilityToken, "eligibilityToken"),
            Required(request.Nickname, "nickname"),
            request.ContactType ?? "phone",
            Required(request.Contact, "contact"),
            Required(request.Password, "password"),
            SupportedLanguages.IsSupported(request.Language) ? request.Language! : SupportedLanguages.Default,
            ct).ConfigureAwait(false);
        return TypedResults.Created((string?)null, Map(result));
    }

    internal static async Task<Ok<SignupVerifiedResponse>> VerifyAsync(VerifyRequest request, HttpContext http, AuthService auth, CancellationToken ct)
    {
        var result = await auth.VerifySignupAsync(request.ChallengeId, Required(request.Code, "code"), http.Request.Headers.UserAgent.ToString(), ct).ConfigureAwait(false);
        return TypedResults.Ok(new SignupVerifiedResponse(Map(result.Tokens), result.RecoveryCodes));
    }

    internal static async Task<Ok<LoginResponse>> LoginAsync(LoginRequest request, HttpContext http, AuthService auth, CancellationToken ct)
    {
        var client = request.Client == "staff" ? "staff" : "patient";
        var result = await auth.LoginAsync(Required(request.Contact, "contact"), Required(request.Password, "password"), client, request.DeviceToken, http.Request.Headers.UserAgent.ToString(), ct).ConfigureAwait(false);
        return result.Tokens is { } tokens
            ? TypedResults.Ok(new LoginResponse("ok", Map(tokens), null, null, null))
            : TypedResults.Ok(new LoginResponse("mfa", null, result.MfaToken, result.Methods, result.MaskedPhone));
    }

    internal static async Task<Ok<MfaResponse>> MfaAsync(MfaRequest request, HttpContext http, AuthService auth, IWebHostEnvironment env, CancellationToken ct)
    {
        var result = await auth.CompleteMfaAsync(Required(request.MfaToken, "mfaToken"), request.Method ?? "totp", Required(request.Code, "code"), request.RememberDevice, http.Request.Headers.UserAgent.ToString(), ct).ConfigureAwait(false);
        var dto = Map(result.Tokens);
        if (IsStaffSession(http, result.Tokens))
        {
            SetStaffCookie(http, env, result.Tokens);
            dto = dto with { RefreshToken = string.Empty };
        }

        return TypedResults.Ok(new MfaResponse(dto, result.DeviceToken));
    }

    internal static async Task<Results<Ok<TokenPairDto>, NoContent>> RefreshAsync(RefreshRequest? request, HttpContext http, AuthService auth, IWebHostEnvironment env, CancellationToken ct)
    {
        var fromCookie = string.IsNullOrEmpty(request?.RefreshToken);
        var token = fromCookie ? http.Request.Cookies[StaffRefreshCookie] : request!.RefreshToken;
        if (string.IsNullOrEmpty(token))
        {
            throw new DomainException(ErrorCodes.SessionRevoked, "No refresh token.");
        }

        var tokens = await auth.RefreshAsync(token, ct).ConfigureAwait(false);
        if (fromCookie)
        {
            SetStaffCookie(http, env, tokens);
            return TypedResults.Ok(Map(tokens) with { RefreshToken = string.Empty });
        }

        return TypedResults.Ok(Map(tokens));
    }

    internal static async Task<NoContent> LogoutAsync(RefreshRequest? request, HttpContext http, AuthService auth, CancellationToken ct)
    {
        var token = string.IsNullOrEmpty(request?.RefreshToken) ? http.Request.Cookies[StaffRefreshCookie] : request!.RefreshToken;
        await auth.LogoutAsync(token, ct).ConfigureAwait(false);
        http.Response.Cookies.Delete(StaffRefreshCookie, new CookieOptions { Path = "/v1/auth" });
        return TypedResults.NoContent();
    }

    private static bool IsStaffSession(HttpContext http, TokenPair tokens)
    {
        var handler = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler();
        var role = handler.ReadJsonWebToken(tokens.AccessToken).GetClaim(Hosting.TamaneenClaims.Role).Value;
        return role != AppRoles.Patient;
    }

    private static void SetStaffCookie(HttpContext http, IWebHostEnvironment env, TokenPair tokens) =>
        http.Response.Cookies.Append(StaffRefreshCookie, tokens.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = !env.IsDevelopment() || http.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/v1/auth",
            Expires = tokens.RefreshExpiresAt,
        });

    private static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new DomainException(ErrorCodes.Validation, $"{field} is required.").With("errors", new Dictionary<string, string[]> { [field] = ["required"] })
            : value;

    private static ChallengeResponse Map(ChallengeResult r) => new(r.ChallengeId, r.MaskedContact, r.ResendAfterSeconds, r.ExpiresInSeconds);

    private static TokenPairDto Map(TokenPair t) => new(t.AccessToken, t.AccessExpiresAt, t.RefreshToken, t.RefreshExpiresAt);
}
