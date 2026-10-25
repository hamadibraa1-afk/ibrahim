using Tamaneen.Api.Endpoints;

namespace Tamaneen.Api.Sessions;

public static class VideoSetup
{
    /// <summary>
    /// Picks the video provider from <c>Video:Provider</c>: "dev" (the default in Development and Testing) or "embed".
    /// Production must name a real provider; the dev test room is refused there.
    /// </summary>
    public static void AddTamaneenVideo(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<VideoOptions>().Bind(builder.Configuration.GetSection(VideoOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton<SessionPassSigner>();
        builder.Services.AddScoped<SessionRoom>();

        var configured = builder.Configuration[$"{VideoOptions.Section}:Provider"];
        var provider = string.IsNullOrWhiteSpace(configured)
            ? builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? DevVideoProvider.ProviderName : string.Empty
            : configured.Trim().ToLowerInvariant();
        switch (provider)
        {
            case DevVideoProvider.ProviderName when builder.Environment.IsProduction():
                throw new InvalidOperationException("Video:Provider=dev (the local test room) is not allowed in Production.");
            case DevVideoProvider.ProviderName:
                builder.Services.AddSingleton<IVideoSessionProvider, DevVideoProvider>();
                break;
            case EmbedVideoProvider.ProviderName:
                builder.Services.AddSingleton<IVideoSessionProvider, EmbedVideoProvider>();
                break;
            default:
                throw new InvalidOperationException("Video:Provider must be \"dev\" or \"embed\".");
        }
    }
}
