using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Tamaneen.SharedKernel;

namespace Tamaneen.Hosting;

public static class AuthSetup
{
    public const string ClinicalPolicy = "clinical";
    public const string PatientPolicy = "patient";

    /// <summary>On-call clinicians who may acknowledge and handle crisis events.</summary>
    public const string CrisisResponderPolicy = "crisis-responder";

    /// <summary>Only supervisors and the clinical lead close crisis events.</summary>
    public const string CrisisCloserPolicy = "crisis-closer";

    public static IServiceCollection AddTamaneenAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthOptions>().Bind(configuration.GetSection(AuthOptions.Section)).ValidateDataAnnotations()
            .Validate(o => !string.IsNullOrWhiteSpace(o.Authority) || HasKey(o.SigningKeyBase64), "Auth needs an Authority or a SigningKeyBase64 of at least 32 bytes.")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthOptions>>((jwt, auth) =>
            {
                var o = auth.Value;
                jwt.MapInboundClaims = false;
                jwt.Authority = string.IsNullOrWhiteSpace(o.Authority) ? null : o.Authority;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = o.Issuer,
                    ValidAudience = o.Audience,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = TamaneenClaims.Subject,
                    RoleClaimType = TamaneenClaims.Role,
                    IssuerSigningKey = HasKey(o.SigningKeyBase64) ? new SymmetricSecurityKey(Convert.FromBase64String(o.SigningKeyBase64!)) : null,
                };

                // Browsers cannot set headers on WebSocket upgrades; SignalR sends the token in the query string.
                var requireSession = o.RequireSession;
                jwt.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async ctx =>
                    {
                        var sid = ctx.Principal?.FindFirst(TamaneenClaims.Session)?.Value;
                        if (sid is null)
                        {
                            if (requireSession)
                            {
                                ctx.Fail("Token has no session.");
                            }

                            return;
                        }

                        var check = ctx.HttpContext.RequestServices.GetService<ISessionCheck>();
                        if (check is null || !Guid.TryParse(sid, out var sessionId) || !await check.IsActiveAsync(sessionId, ctx.HttpContext.RequestAborted).ConfigureAwait(false))
                        {
                            ctx.Fail("Session is no longer active.");
                        }
                    },
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs", StringComparison.Ordinal))
                        {
                            ctx.Token = token;
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(PatientPolicy, p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Patient))
            .AddPolicy(ClinicalPolicy, p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Therapist, AppRoles.Supervisor, AppRoles.ClinicalLead))
            .AddPolicy(CrisisResponderPolicy, p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Therapist, AppRoles.Supervisor, AppRoles.ClinicalLead))
            .AddPolicy(CrisisCloserPolicy, p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Supervisor, AppRoles.ClinicalLead));
        return services;
    }

    private static bool HasKey(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[512];
        return Convert.TryFromBase64String(base64, buffer, out var written) && written >= 32;
    }
}

public static class TamaneenClaims
{
    public const string Subject = "sub";
    public const string Role = "role";
    public const string Pseudonym = "pid";

    /// <summary>Id of the sign-in session (identity.AuthSessions); checked on every request.</summary>
    public const string Session = "sid";
}
