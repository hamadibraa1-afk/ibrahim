using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Tamaneen.SharedKernel;

namespace Tamaneen.Hosting;

public static class LocalizationSetup
{
    /// <summary>Arabic (default) and English, negotiated from Accept-Language only. Resources live under Resources/.</summary>
    public static IServiceCollection AddTamaneenLocalization(this IServiceCollection services)
    {
        services.AddLocalization(o => o.ResourcesPath = "Resources");
        services.Configure<RequestLocalizationOptions>(o =>
        {
            var cultures = SupportedLanguages.All.Select(c => new CultureInfo(c)).ToList();
            o.DefaultRequestCulture = new RequestCulture(SupportedLanguages.Default);
            o.SupportedCultures = cultures;
            o.SupportedUICultures = cultures;

            // "ar-SA" and "en-GB" fall back to "ar" and "en".
            o.FallBackToParentCultures = true;
            o.FallBackToParentUICultures = true;

            // Clients own the user's choice and send it on every request.
            o.RequestCultureProviders = new List<IRequestCultureProvider> { new AcceptLanguageHeaderRequestCultureProvider() };
            o.ApplyCurrentCultureToResponseHeaders = true;
        });
        return services;
    }

    /// <summary>The negotiated language of the current request: "ar" or "en".</summary>
    public static string CurrentLanguage() => SupportedLanguages.Normalize(CultureInfo.CurrentUICulture.Name);

    /// <summary>The language negotiated for this request, independent of the ambient thread culture.</summary>
    public static string RequestLanguage(HttpContext context) =>
        context.Features.Get<IRequestCultureFeature>()?.RequestCulture.UICulture.Name is { Length: > 0 } name
            ? SupportedLanguages.Normalize(name)
            : CurrentLanguage();

    /// <summary>Runs <paramref name="action"/> with the UI culture of <paramref name="language"/>, then restores it.</summary>
    public static T WithLanguage<T>(string language, Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = SupportedLanguages.Culture(language);
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
