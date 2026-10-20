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
        return await _problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Detail = detail.ResourceNotFound ? null : detail.Value,
                Extensions = { ["code"] = domain.Code },
            },
        }).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Domain rule {Code} rejected the request: {Reason}")]
    private static partial void LogRejected(ILogger logger, string code, string reason);
}
