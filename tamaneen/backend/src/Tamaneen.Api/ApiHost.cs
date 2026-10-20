using Tamaneen.Infrastructure.Auth;
using System.Threading.RateLimiting;
using Tamaneen.AiGateway;
using Tamaneen.Api.Auth;
using Tamaneen.Api.Clinical;
using Tamaneen.Api.Gate;
using Tamaneen.Api.Patients;
using Tamaneen.Api.Endpoints;
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
            .WithHeaders("Authorization", "Content-Type", "Accept-Language", "X-Request-Id")
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
            .WithExposedHeaders("Content-Language", "Retry-After")
            // The staff portal's refresh token is an HttpOnly cookie on /v1/auth.
            .AllowCredentials()));

        var app = builder.Build();

        // Localization first: the error handlers below run on the way out and must see the request's culture.
        app.UseRequestLocalization();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
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
        app.MapCareEndpoints();
        app.MapStaffEndpoints();
        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
        {
            // Lets a developer or the end-to-end tests read the code that would have been sent by SMS or e-mail.
            app.MapGet("/dev/outbox", (string to, IMessageSender sender) =>
                sender is InMemoryMessageSender memory && memory.LastTo(to) is { } m ? Results.Ok(new { m.Text, Purpose = m.Purpose.ToString(), m.Language }) : Results.NotFound())
                .AllowAnonymous();
        }

        return app;
    }
}
