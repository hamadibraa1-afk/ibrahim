using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Security;

public static class AuthSetup
{
    public const string ClinicalPolicy = "clinical";
    public const string PatientPolicy = "patient";

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
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(PatientPolicy, p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Patient))
            .AddPolicy(ClinicalPolicy, p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Therapist, AppRoles.Supervisor, AppRoles.ClinicalLead));
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
