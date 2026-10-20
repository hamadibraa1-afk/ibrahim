using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tamaneen.AiGateway.Gateway;
using Tamaneen.Identity;
using Tamaneen.Infrastructure.Ai;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.Infrastructure.Security;
using Tamaneen.SharedKernel;

namespace Tamaneen.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence, encryption and pseudonymization. The host must register a scoped
    /// <see cref="ICurrentUser"/>; every database connection is opened under that identity.
    /// </summary>
    public static IServiceCollection AddTamaneenInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<EncryptionOptions>().Bind(configuration.GetSection(EncryptionOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<PseudonymizationOptions>().Bind(configuration.GetSection(PseudonymizationOptions.Section)).ValidateDataAnnotations().ValidateOnStart();

        services.AddSingleton<IClock, SystemClock>();
        services.AddMemoryCache();
        services.AddScoped<SessionContextInterceptor>();

        services.AddDbContext<TamaneenDbContext>((sp, options) =>
        {
            var db = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            var connectionString = db.ConnectionString;
            if (db.AlwaysEncrypted)
            {
                AlwaysEncryptedSetup.RegisterAzureKeyVaultProvider(new DefaultAzureCredential());
                connectionString = AlwaysEncryptedSetup.EnableOnConnectionString(connectionString);
            }

            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsHistoryTable("__EFMigrationsHistory", Schemas.Security);
                sql.CommandTimeout(db.CommandTimeoutSeconds);
                sql.EnableRetryOnFailure(3);
            });
            options.AddInterceptors(sp.GetRequiredService<SessionContextInterceptor>());
        });

        services.AddSingleton<IKeyEncryptionKeyProvider>(sp => CreateKekProvider(sp.GetRequiredService<IOptions<EncryptionOptions>>().Value, environment));
        services.AddScoped<IPatientKeyStore, PatientKeyStore>();
        services.AddScoped<IPatientDataProtector, PatientDataProtector>();
        services.AddScoped<CryptoShredder>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IAiCallLog, AiCallLog>();
        services.AddSingleton<IContactHasher>(sp =>
        {
            var pepper = DecodeKey(sp.GetRequiredService<IOptions<PseudonymizationOptions>>().Value.ContactPepperBase64, "Pseudonymization:ContactPepperBase64");
            return new ContactHasher(pepper);
        });

        return services;
    }

    internal static IKeyEncryptionKeyProvider CreateKekProvider(EncryptionOptions options, IHostEnvironment environment)
    {
        switch (options.Provider)
        {
            case KeyEncryptionProvider.AzureKeyVault:
                if (!Uri.TryCreate(options.KeyVaultKeyId, UriKind.Absolute, out var keyId))
                {
                    throw Invalid(EncryptionOptions.Section, "Encryption:KeyVaultKeyId must be an absolute key URI.");
                }

                return new AzureKeyVaultKeyEncryptionKeyProvider(keyId, new DefaultAzureCredential());

            case KeyEncryptionProvider.Local:
                if (environment.IsProduction())
                {
                    throw Invalid(EncryptionOptions.Section, "The Local key provider is not allowed in Production. Use AzureKeyVault.");
                }

                return new LocalKeyEncryptionKeyProvider(DecodeKey(options.LocalKeyBase64, "Encryption:LocalKeyBase64"));

            default:
                throw Invalid(EncryptionOptions.Section, $"Unknown key provider {options.Provider}.");
        }
    }

    private static OptionsValidationException Invalid(string name, string message) =>
        new(name, typeof(EncryptionOptions), new[] { message });

    private static byte[] DecodeKey(string? base64, string setting)
    {
        try
        {
            return Convert.FromBase64String(base64 ?? string.Empty) is { Length: >= 32 } bytes
                ? bytes
                : throw new FormatException();
        }
        catch (FormatException)
        {
            throw Invalid(setting, $"{setting} must be base64 of at least 32 bytes.");
        }
    }
}
