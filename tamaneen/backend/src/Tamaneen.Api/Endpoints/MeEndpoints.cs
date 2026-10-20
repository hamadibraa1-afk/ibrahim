using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Tamaneen.Hosting;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Endpoints;

public static class MeEndpoints
{
    public sealed record ChangeLanguageRequest(string? Language);

    public sealed record ChangeLanguageResponse(string Language, string Dir, string Message);

    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        // Saves the user's language so notifications, crisis scripts and AI replies use it.
        app.MapPut("/v1/me/language", ChangeLanguageAsync)
            .RequireAuthorization()
            .WithName("ChangeMyLanguage");
        return app;
    }

    internal static async Task<Results<Ok<ChangeLanguageResponse>, ValidationProblem>> ChangeLanguageAsync(
        ChangeLanguageRequest request,
        ICurrentUser user,
        TamaneenDbContext db,
        IAuditLog audit,
        IStringLocalizer<SharedResource> localizer,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Language))
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["language"] = new[] { localizer["validation.language.required"].Value } },
                detail: localizer["validation.failed"]);
        }

        var account = await db.UserAccounts.FirstOrDefaultAsync(a => a.Id == user.UserId, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Account {user.UserId} not found.");

        account.ChangeLanguage(request.Language);
        audit.Append("user.language_changed", "UserAccount", account.Id.ToString(), reason: account.PreferredLanguage);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The confirmation is written in the language the user just chose.
        var message = LocalizationSetup.WithLanguage(account.PreferredLanguage, () => localizer["message.language_changed"].Value);
        return TypedResults.Ok(new ChangeLanguageResponse(account.PreferredLanguage, SupportedLanguages.Direction(account.PreferredLanguage), message));
    }
}
