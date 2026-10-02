using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using ProposalSystem.Api.Common;
using ProposalSystem.Api.Domain;

namespace ProposalSystem.Api.Security;

public static class SessionDefaults
{
    public const string Scheme = "Session";
    public const string SessionIdClaim = "sid";
    public const string CsrfClaim = "csrf";
    public const string ExpiresClaim = "exp_at";
    /// <summary>Lets the SPA keep its countdown in step with the server's sliding expiry.</summary>
    public const string ExpiresHeader = "X-Session-Expires";
}

/// <summary>
/// Authenticates every request (REST and SignalR alike) from the HttpOnly session cookie.
/// No bearer tokens are accepted, so nothing the page script can read is enough to act as the user.
/// </summary>
public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    SessionService sessions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Cookies[sessions.Options.CookieName];
        if (string.IsNullOrEmpty(token))
            return AuthenticateResult.NoResult();

        var session = await sessions.ValidateAsync(token, Context.RequestAborted);
        if (session is null)
        {
            sessions.ClearCookies(Response);
            return AuthenticateResult.Fail("Session expired or revoked.");
        }

        var user = session.User;
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, user.ArabicName),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(SessionDefaults.SessionIdClaim, session.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(SessionDefaults.CsrfClaim, session.CsrfToken),
            new Claim(SessionDefaults.ExpiresClaim, session.ExpiresAt.ToString("O", CultureInfo.InvariantCulture)),
        };

        var expires = session.ExpiresAt.ToString("O", CultureInfo.InvariantCulture);
        Response.OnStarting(() =>
        {
            Response.Headers[SessionDefaults.ExpiresHeader] = expires;
            return Task.CompletedTask;
        });

        var identity = new ClaimsIdentity(claims, SessionDefaults.Scheme);
        // SignalR reads ExpiresUtc to close long-lived connections when the session ends.
        var properties = new AuthenticationProperties { ExpiresUtc = new DateTimeOffset(session.ExpiresAt, TimeSpan.Zero) };
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), properties, SessionDefaults.Scheme));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        WriteAsync(StatusCodes.Status401Unauthorized, "انتهت الجلسة، الرجاء تسجيل الدخول مجدداً.");

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        WriteAsync(StatusCodes.Status403Forbidden, "ليست لديك صلاحية للوصول إلى هذا المورد.");

    private Task WriteAsync(int status, string message)
    {
        Response.StatusCode = status;
        Response.ContentType = "application/json; charset=utf-8";
        return Response.WriteAsync(JsonSerializer.Serialize(new { message }, ApiJson.Options));
    }
}

/// <summary>
/// CSRF defence for cookie authentication: every state-changing request must echo the session's
/// synchronizer token in a header. A cross-site form or fetch can make the browser send the cookie
/// but can neither read the token nor set the header. SameSite=Strict is the second layer.
/// </summary>
public sealed class CsrfProtectionMiddleware(RequestDelegate next, IOptions<SessionCookieOptions> options)
{
    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    public async Task InvokeAsync(HttpContext context)
    {
        if (!SafeMethods.Contains(context.Request.Method)
            && context.User.Identity?.IsAuthenticated == true
            // SignalR negotiate is a POST; hubs are covered by the Origin check below instead.
            && !context.Request.Path.StartsWithSegments("/hubs"))
        {
            var expected = context.User.FindFirstValue(SessionDefaults.CsrfClaim) ?? "";
            var supplied = context.Request.Headers[options.Value.CsrfHeaderName].ToString();
            if (supplied.Length == 0 || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied)))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync(JsonSerializer.Serialize(
                    new { message = "رمز الحماية غير صالح. أعد تحميل الصفحة وحاول مجدداً." }, ApiJson.Options));
                return;
            }
        }
        await next(context);
    }
}

/// <summary>
/// CORS does not apply to WebSockets, so the hub endpoint checks the Origin itself to stop
/// cross-site WebSocket hijacking.
/// </summary>
public sealed class HubOriginGuardMiddleware(RequestDelegate next, IOptions<CorsSettings> cors)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/hubs"))
        {
            var origin = context.Request.Headers.Origin.ToString();
            if (origin.Length > 0 && !IsAllowed(origin, context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }
        await next(context);
    }

    private bool IsAllowed(string origin, HttpRequest request)
    {
        if (cors.Value.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
            return true;
        // Same origin (SPA served by, or proxied through, the API host).
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
               && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Defence-in-depth headers on every response — the API and, when hosted here, the SPA.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    /// <summary>The API serves JSON and generated print documents only; nothing may load from elsewhere.</summary>
    private const string ApiPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    /// <summary>
    /// The Angular app: own scripts only (no inline script, no eval), Google Fonts for the Arabic
    /// typeface, and same-origin XHR/WebSocket for the API and the SignalR hub.
    /// Inline styles are allowed because Angular injects component styles at runtime.
    /// </summary>
    private const string SpaPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; img-src 'self' data: blob:; connect-src 'self'; " +
        "object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

    public Task InvokeAsync(HttpContext context)
    {
        var isApi = context.Request.Path.StartsWithSegments("/api")
                    || context.Request.Path.StartsWithSegments("/hubs")
                    || context.Request.Path.StartsWithSegments("/health");
        context.Response.OnStarting(() =>
        {
            var h = context.Response.Headers;
            h.XContentTypeOptions = "nosniff";
            h.XFrameOptions = "DENY";
            h["Referrer-Policy"] = "no-referrer";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            h["Cross-Origin-Opener-Policy"] = "same-origin";
            h["Cross-Origin-Resource-Policy"] = "same-site";
            if (!h.ContainsKey("Content-Security-Policy"))
                h.ContentSecurityPolicy = isApi ? ApiPolicy : SpaPolicy;
            // Proposals and personal data must not linger in shared or proxy caches. Hashed SPA
            // bundles may be cached; index.html is revalidated so a deploy is picked up at once.
            if (isApi && !h.ContainsKey("Cache-Control"))
                h.CacheControl = "no-store";
            else if (!isApi && context.Request.Path.Value is "/" or "/index.html")
                h.CacheControl = "no-cache";
            h.Remove("Server");
            h.Remove("X-Powered-By");
            return Task.CompletedTask;
        });
        return next(context);
    }
}

/// <summary>The signed-in user, read from the session claims.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    private ClaimsPrincipal Principal => accessor.HttpContext?.User ?? new ClaimsPrincipal();

    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true;

    public int Id => int.Parse(Principal.FindFirstValue(ClaimTypes.NameIdentifier)
                               ?? throw new ApiException(StatusCodes.Status401Unauthorized, "انتهت الجلسة."), CultureInfo.InvariantCulture);

    public string Name => Principal.FindFirstValue(ClaimTypes.Name) ?? "";

    public UserRole Role => Enum.Parse<UserRole>(Principal.FindFirstValue(ClaimTypes.Role) ?? nameof(UserRole.Employee));

    public long SessionId => long.Parse(Principal.FindFirstValue(SessionDefaults.SessionIdClaim) ?? "0", CultureInfo.InvariantCulture);

    public bool IsAdmin => Role == UserRole.Admin;

    public bool IsReviewer => Role is UserRole.Screener or UserRole.CommitteeMember or UserRole.Admin;
}
