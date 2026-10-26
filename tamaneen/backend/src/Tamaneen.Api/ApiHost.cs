using System.Net;
using Tamaneen.Infrastructure.Auth;
using System.Threading.RateLimiting;
using Tamaneen.AiGateway;
using Tamaneen.Api.Auth;
using Tamaneen.Api.Clinical;
using Tamaneen.Api.Gate;
using Tamaneen.Api.Notifications;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tamaneen.SharedKernel.Notifications;
using Tamaneen.Api.Patients;
using Tamaneen.Api.Endpoints;
using Tamaneen.Api.Sessions;
using Tamaneen.Hosting;
using Tamaneen.Infrastructure;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api;

public static class ApiHost
{
    public const string CorsPolicy = "tamaneen-frontends";

    public const string PatientRateLimitPolicy = "patient-ai";

    private static readonly string[] ReadyTags = { "ready" };

    /// <summary>During maintenance only the patient's own endpoints pause; config, sign-in, crisis help and the staff portal stay up.</summary>
    internal static bool MaintenanceBlocks(PathString path, PlatformOptions platform) =>
        platform.Maintenance
        && path.StartsWithSegments("/v1", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWithSegments("/v1/meta", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWithSegments("/v1/auth", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWithSegments("/v1/crisis", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWithSegments("/v1/staff", StringComparison.OrdinalIgnoreCase);

    private static void AddMessageSender(WebApplicationBuilder builder)
    {
        var url = builder.Configuration["Messaging:WebhookUrl"];
        if (!string.IsNullOrWhiteSpace(url))
        {
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && builder.Environment.IsProduction())
            {
                throw new InvalidOperationException("Messaging:WebhookUrl must use HTTPS in Production.");
            }

            builder.Services.AddHttpClient<IMessageSender, WebhookMessageSender>(c => c.Timeout = TimeSpan.FromSeconds(10));
            return;
        }

        if (builder.Environment.IsProduction())
        {
            throw new InvalidOperationException("Messaging:WebhookUrl is required in Production (sign-in codes cannot be delivered otherwise).");
        }

        builder.Services.AddSingleton<IMessageSender, InMemoryMessageSender>();
    }

    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<RequestCurrentUser>();
        builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<RequestCurrentUser>());
        builder.Services.AddTamaneenInfrastructure(builder.Configuration, builder.Environment);
        builder.Services.AddTamaneenAiGateway(builder.Configuration, builder.Environment);
        builder.Services.AddTamaneenLocalization();
        builder.Services.AddTamaneenProblemDetails<SharedResource>();
        builder.Services.AddTamaneenAuth(builder.Configuration);
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddScoped<ScopeRunner>();
        builder.Services.AddOptions<PlatformOptions>().Bind(builder.Configuration.GetSection(PlatformOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddOptions<ServerSecretsOptions>().Bind(builder.Configuration.GetSection(ServerSecretsOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddOptions<MessagingOptions>().Bind(builder.Configuration.GetSection(MessagingOptions.Section));
        builder.Services.AddOptions<CrisisGateOptions>().Bind(builder.Configuration.GetSection(CrisisGateOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton<ServerSecrets>();
        builder.Services.AddSingleton<PasswordService>();
        builder.Services.AddSingleton<TokenService>();
        builder.Services.AddSingleton<MessageComposer>();
        builder.Services.AddSingleton(QuestionnaireCatalog.LoadEmbedded());
        builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<PatientContext>();
        builder.Services.AddScoped<TherapistDirectory>();
        builder.Services.AddScoped<WaitlistNotifier>();

        // The notification service (P46, S41). Registered before the modules' TryAdd of NullNotifier, so this one wins.
        builder.Services.AddOptions<NotificationOptions>().Bind(builder.Configuration.GetSection(NotificationOptions.Section))
            .PostConfigure(o =>
            {
                if (builder.Environment.IsEnvironment("Testing") && builder.Configuration[$"{NotificationOptions.Section}:WorkerEnabled"] is null)
                {
                    o.WorkerEnabled = false;
                }
            });
        builder.Services.AddScoped<NotificationTexts>();
        builder.Services.AddSingleton<Notifier>();
        builder.Services.AddSingleton<INotifier>(sp => sp.GetRequiredService<Notifier>());
        builder.Services.TryAddSingleton<INotifier, NullNotifier>();
        builder.Services.AddSingleton<NotificationWorker>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<NotificationWorker>());
        builder.AddTamaneenVideo();
        builder.Services.AddScoped<AppointmentNotices>();
        builder.Services.AddHttpClient("crisis-console");
        builder.Services.AddScoped<StepUpVerifier>();
        if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
        {
            builder.Services.AddSingleton<Dev.DevSeeder>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<Dev.DevSeeder>());
        }
        builder.Services.AddHttpClient<CrisisGateClient>();
        AddMessageSender(builder);
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(AuthEndpoints.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimits:AuthPerMinute", 30), Window = TimeSpan.FromMinutes(1) }));
            o.AddPolicy(PatientRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                http.User.FindFirst(TamaneenClaims.Subject)?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimits:AiPerMinute", 20), Window = TimeSpan.FromMinutes(1) }));
        });
        builder.Services.AddHealthChecks().AddDbContextCheck<TamaneenDbContext>("database", tags: ReadyTags);

        var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>();
        builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p => p
            .WithOrigins(origins)
            .WithHeaders("Authorization", "Content-Type", "Accept-Language", "X-Request-Id", "If-Match")
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
            .WithExposedHeaders("Content-Language", "Retry-After", "ETag")
            // The staff portal's refresh token is an HttpOnly cookie on /v1/auth.
            .AllowCredentials()));

        var app = builder.Build();

        // Localization first: the error handlers below run on the way out and must see the request's culture.
        app.UseRequestLocalization();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.Use(async (context, next) =>
        {
            if (MaintenanceBlocks(context.Request.Path, context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<PlatformOptions>>().CurrentValue))
            {
                throw new DomainException(ErrorCodes.Maintenance, "Scheduled maintenance.");
            }

            await next(context).ConfigureAwait(false);
        });
        app.UseCors(CorsPolicy);
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
        app.MapMetaEndpoints();
        app.MapMeEndpoints();
        app.MapAuthEndpoints();
        app.MapIntakeEndpoints();
        app.MapSchedulingEndpoints();
        app.MapWaitlistEndpoints();
        app.MapCareEndpoints();
        app.MapStaffEndpoints();
        app.MapStaffScheduleEndpoints();
        app.MapAvailabilityEndpoints();
        app.MapStaffAccessEndpoints();
        app.MapSessionEndpoints();
        app.MapNotificationEndpoints();
        app.MapAlertsEndpoints();
        app.MapCrisisFollowUpEndpoints();
        app.MapStaffProfileEndpoints();
        app.MapStaffSettingsEndpoints();
        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
        {
            // Lets a developer or the end-to-end tests read the code that would have been sent by SMS or e-mail.
            app.MapGet("/dev/outbox", (string to, IMessageSender sender) =>
                sender is InMemoryMessageSender memory && memory.LastTo(to) is { } m ? Results.Ok(new { m.Text, Purpose = m.Purpose.ToString(), m.Language }) : Results.NotFound())
                .AllowAnonymous();

            // A page for people trying the app locally: the latest messages that would have gone out by SMS or e-mail.
            app.MapGet("/dev/inbox", (IMessageSender sender) =>
            {
                var rows = sender is InMemoryMessageSender memory
                    ? string.Concat(memory.Messages.Reverse().Take(30).Select(m =>
                        $"<tr><td dir=\"ltr\">{WebUtility.HtmlEncode(m.To)}</td><td>{WebUtility.HtmlEncode(m.Purpose.ToString())}</td><td>{WebUtility.HtmlEncode(m.Text)}</td></tr>"))
                    : string.Empty;
                var html = "<!doctype html><html lang=\"ar\" dir=\"rtl\"><head><meta charset=\"utf-8\"><meta http-equiv=\"refresh\" content=\"5\">"
                    + "<title>Tamaneen test inbox</title><style>body{font-family:system-ui;margin:24px}td,th{border:1px solid #ccc;padding:8px;text-align:start}table{border-collapse:collapse}</style></head><body>"
                    + "<h1>صندوق الرسائل التجريبي · Test inbox</h1><p>رموز التحقق التي كانت ستُرسل بالجوال أو البريد (تتحدث كل 5 ثوانٍ). Codes that would have been sent; refreshes every 5 s.</p>"
                    + $"<table><tr><th>إلى · To</th><th>النوع · Purpose</th><th>الرسالة · Message</th></tr>{rows}</table></body></html>";
                return Results.Content(html, "text/html; charset=utf-8");
            }).AllowAnonymous();
        }

        return app;
    }
}
