using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Tamaneen.SharedKernel;

namespace Tamaneen.Hosting;

/// <summary>Turns <see cref="DomainException"/> into a localized ProblemDetails with the right status code.</summary>
public sealed partial class DomainExceptionHandler<TResource> : IExceptionHandler
{
    private static readonly Dictionary<string, int> StatusByCode = new(StringComparer.Ordinal)
    {
        [ErrorCodes.UnsupportedLanguage] = StatusCodes.Status400BadRequest,
        [ErrorCodes.InvalidPseudonym] = StatusCodes.Status400BadRequest,
        [ErrorCodes.ReasonRequired] = StatusCodes.Status400BadRequest,
        [ErrorCodes.NotFound] = StatusCodes.Status404NotFound,
        [ErrorCodes.CrisisAlreadyAcknowledged] = StatusCodes.Status409Conflict,
        [ErrorCodes.AssignmentAlreadyEnded] = StatusCodes.Status409Conflict,
        [ErrorCodes.CrisisClosed] = StatusCodes.Status409Conflict,
        [ErrorCodes.CrisisCloseNotAllowed] = StatusCodes.Status403Forbidden,
        [ErrorCodes.PatientDataShredded] = StatusCodes.Status410Gone,
        [ErrorCodes.Validation] = StatusCodes.Status400BadRequest,
        [ErrorCodes.AiMessageInvalid] = StatusCodes.Status400BadRequest,
        [ErrorCodes.WeakPassword] = StatusCodes.Status400BadRequest,
        [ErrorCodes.InvalidTime] = StatusCodes.Status400BadRequest,
        [ErrorCodes.InvalidTimeZone] = StatusCodes.Status400BadRequest,
        [ErrorCodes.InvalidCarePath] = StatusCodes.Status400BadRequest,
        [ErrorCodes.ConsentUnknown] = StatusCodes.Status400BadRequest,
        [ErrorCodes.InvalidCredentials] = StatusCodes.Status401Unauthorized,
        [ErrorCodes.SessionRevoked] = StatusCodes.Status401Unauthorized,
        [ErrorCodes.InvalidCode] = StatusCodes.Status400BadRequest,
        [ErrorCodes.MfaFailed] = StatusCodes.Status400BadRequest,
        [ErrorCodes.PasswordMismatch] = StatusCodes.Status400BadRequest,
        [ErrorCodes.ChallengeExpired] = StatusCodes.Status410Gone,
        [ErrorCodes.AccountLocked] = StatusCodes.Status423Locked,
        [ErrorCodes.ResendTooSoon] = StatusCodes.Status429TooManyRequests,
        [ErrorCodes.StepUpRequired] = StatusCodes.Status403Forbidden,
        [ErrorCodes.NotEligible] = StatusCodes.Status403Forbidden,
        [ErrorCodes.BookingNotEligible] = StatusCodes.Status403Forbidden,
        [ErrorCodes.AiConsentWithdrawn] = StatusCodes.Status403Forbidden,
        [ErrorCodes.ConsentsMissing] = StatusCodes.Status403Forbidden,
        [ErrorCodes.CrisisLocked] = StatusCodes.Status409Conflict,
        [ErrorCodes.SlotUnavailable] = StatusCodes.Status409Conflict,
        [ErrorCodes.HoldExpired] = StatusCodes.Status409Conflict,
        [ErrorCodes.AppointmentNotChangeable] = StatusCodes.Status409Conflict,
        [ErrorCodes.TriageAlreadyReviewed] = StatusCodes.Status409Conflict,
        [ErrorCodes.TotpAlreadyEnabled] = StatusCodes.Status409Conflict,
        [ErrorCodes.IntakeClosed] = StatusCodes.Status409Conflict,
        [ErrorCodes.DeletionBlockedByCrisis] = StatusCodes.Status409Conflict,
        [ErrorCodes.CrisisServiceUnavailable] = StatusCodes.Status503ServiceUnavailable,
    };

    private readonly IProblemDetailsService _problems;
    private readonly IStringLocalizer<TResource> _localizer;
    private readonly ILogger<DomainExceptionHandler<TResource>> _log;

    public DomainExceptionHandler(IProblemDetailsService problems, IStringLocalizer<TResource> localizer, ILogger<DomainExceptionHandler<TResource>> log)
    {
        _problems = problems;
        _localizer = localizer;
        _log = log;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (exception is not DomainException domain)
        {
            return false;
        }

        var status = StatusByCode.GetValueOrDefault(domain.Code, StatusCodes.Status422UnprocessableEntity);
        LogRejected(_log, domain.Code, domain.Message);
        httpContext.Response.StatusCode = status;
        var detail = LocalizationSetup.WithLanguage(LocalizationSetup.RequestLanguage(httpContext), () => _localizer[domain.Code, domain.Arguments.ToArray()]);
        var problem = new ProblemDetails
        {
            Status = status,
            Detail = detail.ResourceNotFound ? null : detail.Value,
            Extensions = { ["code"] = domain.Code },
        };
        foreach (var (key, value) in domain.Extras)
        {
            // Field errors carry codes ("required", "invalid"); the client gets them as readable text.
            problem.Extensions[key] = value is IDictionary<string, string[]> fields
                ? LocalizationSetup.WithLanguage(LocalizationSetup.RequestLanguage(httpContext), () => fields.ToDictionary(f => f.Key, f => f.Value.Select(c => _localizer[$"validation.{c}"].Value).ToArray()))
                : value;
        }

        if (domain.Extras.TryGetValue("retryAfterSeconds", out var retry))
        {
            httpContext.Response.Headers.RetryAfter = Convert.ToString(retry, System.Globalization.CultureInfo.InvariantCulture);
        }

        return await _problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        }).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Domain rule {Code} rejected the request: {Reason}")]
    private static partial void LogRejected(ILogger logger, string code, string reason);
}
