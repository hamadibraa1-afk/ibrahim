using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tamaneen.AiGateway.Deidentification;
using Tamaneen.AiGateway.Gateway;
using Tamaneen.AiGateway.Model;
using Tamaneen.AiGateway.Prompts;
using Tamaneen.AiGateway.Validation;

namespace Tamaneen.AiGateway;

public static class AiGatewayServiceCollectionExtensions
{
    /// <summary>
    /// Registers the gateway. The host must also register an <see cref="IAiCallLog"/> (Infrastructure does).
    /// The language model defaults to Claude; tests replace <see cref="ILanguageModel"/>.
    /// </summary>
    public static IServiceCollection AddTamaneenAiGateway(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsProduction() && string.IsNullOrWhiteSpace(configuration["Anthropic:ApiKey"]))
        {
            throw new InvalidOperationException("Anthropic:ApiKey must be configured in Production.");
        }

        services.AddOptions<AiGatewayOptions>().Bind(configuration.GetSection(AiGatewayOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<AnthropicOptions>().Bind(configuration.GetSection("Anthropic"));
        services.AddOptions<PresidioOptions>().Bind(configuration.GetSection("Presidio"));
        services.AddOptions<OutputValidatorOptions>().Bind(configuration.GetSection("AiGateway:Validator"));

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(PromptCatalog.LoadEmbedded());
        services.AddSingleton<PatternPiiDetector>();
        services.AddHttpClient<PresidioPiiDetector>();
        services.AddTransient<IPiiDetector>(sp => sp.GetRequiredService<PresidioPiiDetector>());
        services.AddScoped<Deidentifier>();
        services.AddSingleton<OutputValidator>();
        services.AddSingleton<AiResiliencePipeline>();
        services.TryAddSingleton<ILanguageModel>(sp =>
        {
            var anthropic = sp.GetRequiredService<IOptions<AnthropicOptions>>();
            return string.IsNullOrWhiteSpace(anthropic.Value.ApiKey)
                ? new NotConfiguredLanguageModel(anthropic.Value.Model)
                : new AnthropicLanguageModel(anthropic);
        });
        services.AddScoped<IAiGateway, AiGatewayService>();
        return services;
    }
}
