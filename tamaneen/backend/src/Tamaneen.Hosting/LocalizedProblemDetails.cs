using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Tamaneen.SharedKernel;

namespace Tamaneen.Hosting;

/// <summary>
/// Every error leaves a Tamaneen host as RFC 7807 ProblemDetails with a title in the request's language,
/// a stable machine code, the trace id, and the language and direction the client should render with.
/// <typeparamref name="TResource"/> must provide problem.{status}.title, problem.default.title,
/// problem.crisis_hint and one entry per <see cref="ErrorCodes"/> value.
/// </summary>
public static class LocalizedProblemDetails
{
    public static IServiceCollection AddTamaneenProblemDetails<TResource>(this IServiceCollection services)
    {
        services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
        {
            var localizer = ctx.HttpContext.RequestServices.GetRequiredService<IStringLocalizer<TResource>>();
            var language = LocalizationSetup.RequestLanguage(ctx.HttpContext);
            var status = ctx.ProblemDetails.Status ?? StatusCodes.Status500InternalServerError;
            LocalizationSetup.WithLanguage(language, () =>
            {
                var title = localizer[$"problem.{status}.title"];
                ctx.ProblemDetails.Title = title.ResourceNotFound ? localizer["problem.default.title"] : title;
                if (status >= StatusCodes.Status500InternalServerError)
                {
                    // Nobody may be left without a way to get help when something fails.
                    ctx.ProblemDetails.Extensions["crisisHint"] = localizer["problem.crisis_hint"].Value;
                }

                return 0;
            });
            ctx.ProblemDetails.Type ??= $"https://tamaneen.app/problems/{status}";
            ctx.ProblemDetails.Instance ??= ctx.HttpContext.Request.Path;
            ctx.ProblemDetails.Extensions["lang"] = language;
            ctx.ProblemDetails.Extensions["dir"] = SupportedLanguages.Direction(language);
            ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.Features.Get<IHttpActivityFeature>()?.Activity.TraceId.ToString()
                ?? ctx.HttpContext.TraceIdentifier;
        });
        services.AddExceptionHandler<DomainExceptionHandler<TResource>>();
        return services;
    }
}
