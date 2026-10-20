using Microsoft.Extensions.Localization;
using Tamaneen.Api.Localization;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Endpoints;

public static class MetaEndpoints
{
    public sealed record LanguageInfo(string Code, string Name, string Dir);

    public sealed record LocaleResponse(string Language, string Dir, string Message, IReadOnlyList<LanguageInfo> Supported);

    public static IEndpointRouteBuilder MapMetaEndpoints(this IEndpointRouteBuilder app)
    {
        // Public: lets both frontends confirm the negotiated language and render the language switch.
        app.MapGet("/v1/meta/locale", (IStringLocalizer<SharedResource> localizer) =>
        {
            var language = LocalizationSetup.CurrentLanguage();
            var supported = SupportedLanguages.All
                .Select(code => new LanguageInfo(code, localizer[$"language.{code}"], SupportedLanguages.Direction(code)))
                .ToList();
            return TypedResults.Ok(new LocaleResponse(language, SupportedLanguages.Direction(language), localizer["message.welcome"], supported));
        })
        .AllowAnonymous()
        .WithName("GetLocale");

        return app;
    }
}
