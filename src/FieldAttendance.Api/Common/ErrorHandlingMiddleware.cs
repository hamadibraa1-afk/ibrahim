using FieldAttendance.Domain.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Common;

public sealed class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DomainException ex)
        {
            await Write(context, StatusCodes.Status400BadRequest, new ApiError(ex.Code, ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            await Write(context, StatusCodes.Status409Conflict,
                new ApiError("concurrency", "This record was changed by another user. Reload and try again."));
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            await Write(context, StatusCodes.Status409Conflict, new ApiError("duplicate", "A record with the same unique value already exists."));
        }
        catch (UnauthorizedAccessException)
        {
            await Write(context, StatusCodes.Status401Unauthorized, new ApiError("unauthorized", "Authentication required."));
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // client went away; nothing to write
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error on {Method} {Path}", context.Request.Method, context.Request.Path);
            await Write(context, StatusCodes.Status500InternalServerError, new ApiError("server_error", "Unexpected server error."));
        }
    }

    private static Task Write(HttpContext context, int status, ApiError error)
    {
        if (context.Response.HasStarted) return Task.CompletedTask;
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(error);
    }
}
