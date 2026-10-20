using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Tamaneen.Crisis.Assessment;
using Tamaneen.Crisis.Gate;
using Tamaneen.Crisis.Rules;
using Tamaneen.Crisis.Worker.Endpoints;
using Tamaneen.Crisis.Worker.Escalation;
using Tamaneen.Crisis.Worker.Hubs;
using Tamaneen.Crisis.Worker.Paging;
using Tamaneen.Hosting;
using Tamaneen.Infrastructure;
using Tamaneen.Infrastructure.Crisis;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;

namespace Tamaneen.Crisis.Worker;

public static class WorkerHost
{
    public const string CorsPolicy = "crisis-console";

    private static readonly string[] ReadyTags = { "ready" };

    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);
        var services = builder.Services;

        services.AddHttpContextAccessor();
        services.AddScoped<RequestCurrentUser>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<RequestCurrentUser>());
        services.AddTamaneenInfrastructure(builder.Configuration, builder.Environment);
        services.AddTamaneenLocalization();
        services.AddTamaneenProblemDetails<CrisisResource>();
        services.AddTamaneenAuth(builder.Configuration);

        services.AddOptions<CrisisSlaOptions>().Bind(builder.Configuration.GetSection(CrisisSlaOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<CrisisContactOptions>().Bind(builder.Configuration.GetSection(CrisisContactOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<WorkerOptions>().Bind(builder.Configuration.GetSection(WorkerOptions.Section)).ValidateDataAnnotations()
            .Validate(o => o.ServiceUserId != Guid.Empty, "Crisis:Worker:ServiceUserId is required.")
            .Validate(o => IsKey(o.ServiceKeyBase64), "Crisis:Worker:ServiceKeyBase64 must be base64 of at least 32 bytes.")
            .ValidateOnStart();
        services.AddOptions<PagingOptions>().Bind(builder.Configuration.GetSection(PagingOptions.Section)).ValidateDataAnnotations()
            .Validate(o => string.IsNullOrWhiteSpace(o.WebhookUrl) || Uri.TryCreate(o.WebhookUrl, UriKind.Absolute, out _), "Crisis:Paging:WebhookUrl must be an absolute URL.")
            .Validate(o => string.IsNullOrWhiteSpace(o.WebhookUrl) || IsKey(o.WebhookSecretBase64), "Crisis:Paging:WebhookSecretBase64 must be set with the webhook URL.")
            .Validate(o => !builder.Environment.IsProduction() || Uri.TryCreate(o.WebhookUrl, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps,
                "In Production the worker refuses to start without an HTTPS paging channel (Crisis:Paging:WebhookUrl).")
            .ValidateOnStart();

        // Rule packs are validated when the process starts: a broken pack never serves traffic.
        var packs = CrisisRuleCatalog.LoadTextPacks();
        services.AddSingleton<IRiskEvaluator>(new RiskEvaluator(packs, CrisisRuleCatalog.LoadQuestionnaires()));
        services.AddSingleton(CrisisRuleCatalog.LoadScripts());
        services.AddSingleton(new RuleCategoryIndex(packs));

        services.AddScoped<ICrisisCaseStore, CrisisCaseStore>();
        services.AddScoped<ICrisisAlertPublisher, SignalRCrisisAlertPublisher>();
        services.AddScoped<CrisisAlertFactory>();
        services.AddScoped<CrisisGate>();
        services.AddScoped<EscalationService>();
        services.AddScoped<PageDispatcher>();
        services.AddHttpClient<IPagerChannel, WebhookPagerChannel>();
        services.AddSingleton<LoopHeartbeat>();
        if (builder.Configuration.GetValue("Crisis:Worker:RunBackgroundLoops", true))
        {
            services.AddHostedService<EscalationLoop>();
            services.AddHostedService<PagingLoop>();
        }

        services.AddSignalR();
        services.AddHealthChecks()
            .AddDbContextCheck<TamaneenDbContext>("database", tags: ReadyTags)
            .AddCheck<LoopsHealthCheck>("loops", tags: ReadyTags);

        var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>();
        services.AddCors(o => o.AddPolicy(CorsPolicy, p => p
            .WithOrigins(origins)
            .WithHeaders("Authorization", "Content-Type", "Accept-Language", "X-Requested-With", "X-SignalR-User-Agent")
            .WithMethods("GET", "POST")
            .AllowCredentials()));

        var app = builder.Build();
        app.UseRequestLocalization();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseCors(CorsPolicy);
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
        app.MapGateEndpoints();
        app.MapConsoleEndpoints();
        app.MapHub<CrisisHub>(CrisisHub.Path);
        return app;
    }

    private static bool IsKey(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[512];
        return Convert.TryFromBase64String(base64, buffer, out var written) && written >= 32;
    }
}
