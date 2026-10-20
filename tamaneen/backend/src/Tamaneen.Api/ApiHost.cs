using Tamaneen.AiGateway;
using Tamaneen.Api.Endpoints;
using Tamaneen.Hosting;
using Tamaneen.Infrastructure;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api;

public static class ApiHost
{
    public const string CorsPolicy = "tamaneen-frontends";

    private static readonly string[] ReadyTags = { "ready" };

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
        builder.Services.AddHealthChecks().AddDbContextCheck<TamaneenDbContext>("database", tags: ReadyTags);

        var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>();
        builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p => p
            .WithOrigins(origins)
            .WithHeaders("Authorization", "Content-Type", "Accept-Language", "X-Request-Id")
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
            .WithExposedHeaders("Content-Language")));

        var app = builder.Build();

        // Localization first: the error handlers below run on the way out and must see the request's culture.
        app.UseRequestLocalization();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseCors(CorsPolicy);
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
        app.MapMetaEndpoints();
        app.MapMeEndpoints();
        return app;
    }
}
