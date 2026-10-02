using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using ProposalSystem.Api.Common;
using ProposalSystem.Api.Data;
using ProposalSystem.Api.Realtime;
using ProposalSystem.Api.Security;
using ProposalSystem.Api.Workflow;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

// ---------------------------------------------------------------- options
// Settings that contradict each other would silently break sessions, escalation or impact
// windows, so they stop the application at startup instead.
builder.Services.AddOptions<SessionCookieOptions>().Bind(config.GetSection("Security:Session"))
    .Validate(o => o.IdleTimeoutHours > 0 && o.IdleTimeoutHours <= o.AbsoluteLifetimeHours,
        "Security:Session: IdleTimeoutHours must be positive and no longer than AbsoluteLifetimeHours.")
    .Validate(o => o.MaxFailedLogins > 0 && o.LockoutMinutes > 0,
        "Security:Session: MaxFailedLogins and LockoutMinutes must be positive.")
    .ValidateOnStart();
builder.Services.Configure<CorsSettings>(config.GetSection("Security:Cors"));
builder.Services.Configure<BlindReviewOptions>(config.GetSection("BlindReview"));
builder.Services.AddOptions<SlaOptions>().Bind(config.GetSection("Sla"))
    .Validate(o => o.CheckIntervalMinutes > 0 && o.ScreeningHours > 0 && o.CommitteeHours > 0 && o.ExecutiveHours > 0
                   && o.ReturnedForEditHours > 0 && o.ReminderRepeatHours > 0,
        "Sla: the check interval, every stage deadline and ReminderRepeatHours must be positive.")
    .Validate(o => o.Escalation.Level1AfterHours > 0 && o.Escalation.Level2AfterHours > o.Escalation.Level1AfterHours,
        "Sla:Escalation: Level2AfterHours must be greater than Level1AfterHours, and both positive.")
    .ValidateOnStart();
builder.Services.AddOptions<ImpactTrackingOptions>().Bind(config.GetSection("ImpactTracking"))
    .Validate(o => o.WindowOpensAfterMonths >= 0 && o.DueAfterMonths > o.WindowOpensAfterMonths,
        "ImpactTracking: DueAfterMonths must be later than WindowOpensAfterMonths.")
    .ValidateOnStart();
builder.Services.Configure<AttachmentOptions>(config.GetSection("Attachments"));

// ---------------------------------------------------------------- data
var provider = config.GetValue("DatabaseProvider", "Sqlite");
builder.Services.AddDbContext<AppDbContext>(o =>
{
    if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        o.UseSqlServer(config.GetConnectionString("SqlServer"), sql => sql.EnableRetryOnFailure(3));
    else
        o.UseSqlite(config.GetConnectionString("Sqlite"));
});

// ---------------------------------------------------------------- application services
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddSingleton<IdentityRevealPolicy>();
builder.Services.AddSingleton<ProposalAccess>();
builder.Services.AddSingleton<ProposalMapper>();
builder.Services.AddScoped<AuditTrail>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<CustomFieldWriter>();
builder.Services.AddScoped<CommitteeProgress>();
builder.Services.AddScoped<JobLeaseService>();
builder.Services.AddScoped<SlaEscalationService>();
builder.Services.AddHostedService<SlaMonitorService>();

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ---------------------------------------------------------------- real-time
builder.Services.AddSignalR(o =>
    {
        o.EnableDetailedErrors = builder.Environment.IsDevelopment();
        // Clients never send payloads; keep the window for abuse small.
        o.MaximumReceiveMessageSize = 32 * 1024;
    })
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ---------------------------------------------------------------- authentication: HttpOnly session cookie
builder.Services.AddAuthentication(SessionDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionDefaults.Scheme, null);
builder.Services.AddAuthorization(o =>
{
    // Deny by default: an endpoint without [AllowAnonymous] always needs a live session.
    o.FallbackPolicy = new AuthorizationPolicyBuilder(SessionDefaults.Scheme).RequireAuthenticatedUser().Build();
});

// ---------------------------------------------------------------- CORS: explicit origins only
var cors = config.GetSection("Security:Cors").Get<CorsSettings>() ?? new CorsSettings();
foreach (var origin in cors.AllowedOrigins)
{
    if (origin.Contains('*', StringComparison.Ordinal) || !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.AbsolutePath != "/")
        throw new InvalidOperationException($"Security:Cors:AllowedOrigins must hold exact origins like https://suggestions.shjcharity.ae — got '{origin}'.");
    if (!builder.Environment.IsDevelopment() && uri.Scheme != Uri.UriSchemeHttps)
        throw new InvalidOperationException($"Only HTTPS origins are allowed outside Development — got '{origin}'.");
}
builder.Services.AddCors(o => o.AddPolicy("spa", p => p
    .WithOrigins(cors.AllowedOrigins)
    .WithMethods("GET", "POST", "PUT", "DELETE")
    .WithHeaders("Content-Type", "X-XSRF-TOKEN", "X-Requested-With", "X-SignalR-User-Agent")
    .WithExposedHeaders(SessionDefaults.ExpiresHeader, "Content-Disposition")
    .AllowCredentials()
    .SetPreflightMaxAge(TimeSpan.FromMinutes(cors.PreflightMaxAgeMinutes))));

// ---------------------------------------------------------------- HSTS
var hsts = config.GetSection("Security:Hsts").Get<HstsSettings>() ?? new HstsSettings();
builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(hsts.MaxAgeDays);
    o.IncludeSubDomains = hsts.IncludeSubDomains;
    o.Preload = hsts.Preload;
});

// ---------------------------------------------------------------- brute-force protection
// Per-IP login throttle on top of the per-account lockout. Offices behind one NAT address share
// the budget, so it is configurable (Security:LoginRateLimit).
var loginPermits = Math.Max(1, config.GetValue("Security:LoginRateLimit:PermitLimit", 10));
var loginWindow = TimeSpan.FromMinutes(Math.Max(1, config.GetValue("Security:LoginRateLimit:WindowMinutes", 5)));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginPermits, Window = loginWindow }));
});

if (config.GetValue<bool>("Security:BehindReverseProxy"))
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        foreach (var ip in config.GetSection("Security:KnownProxies").Get<string[]>() ?? [])
            o.KnownProxies.Add(System.Net.IPAddress.Parse(ip));
    });
}

var app = builder.Build();

// ---------------------------------------------------------------- database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    await DbSeeder.EnsureFormFieldsAsync(db);

    if (args.Contains("--seed-passwords", StringComparer.OrdinalIgnoreCase))
    {
        var count = await DbSeeder.SeedPasswordsAsync(db);
        app.Logger.LogWarning("Set the demo password on {Count} account(s). Change them before real use.", count);
        return;
    }
    if (app.Environment.IsDevelopment() && config.GetValue<bool>("SeedDemoData"))
        await DbSeeder.SeedDemoUsersAsync(db, TimeProvider.System);
}

// ---------------------------------------------------------------- pipeline
if (config.GetValue<bool>("Security:BehindReverseProxy"))
    app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<ErrorHandlingMiddleware>();

// Production hosting: the built Angular app is copied to wwwroot and served from the same
// origin, which is what lets the SameSite=Strict cookie and the XSRF header work unchanged.
var spaHosted = File.Exists(Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "index.html"));
if (spaHosted)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}
app.UseRouting();
app.UseCors("spa");
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<HubOriginGuardMiddleware>();
app.UseMiddleware<CsrfProtectionMiddleware>();
app.UseAuthorization();

app.MapControllers();
// A live socket must not outlive the session that opened it: it is closed when the session's
// expiry passes, and the client's reconnect re-authenticates (and fails if signed out).
app.MapHub<NotificationHub>("/hubs/notifications", o => o.CloseOnAuthenticationExpiration = true);
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
if (spaHosted)
{
    // Client-side routes (/proposals/12, /impact...) load the SPA; unknown /api and /hubs paths stay 404.
    app.MapFallbackToFile("{**path:regex(^(?!api/|hubs/).*$)}", "index.html").AllowAnonymous();
}

await app.RunAsync();

public partial class Program;
