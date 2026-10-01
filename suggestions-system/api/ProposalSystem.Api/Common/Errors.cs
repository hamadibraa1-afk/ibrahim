using System.Text.Json;

namespace ProposalSystem.Api.Common;

/// <summary>A request the API refuses on purpose. The message is user-facing (Arabic).</summary>
public sealed class ApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public static ApiException BadRequest(string message) => new(StatusCodes.Status400BadRequest, message);
    public static ApiException Forbidden(string message = "ليست لديك صلاحية لتنفيذ هذا الإجراء.") => new(StatusCodes.Status403Forbidden, message);
    public static ApiException NotFound(string message = "العنصر المطلوب غير موجود.") => new(StatusCodes.Status404NotFound, message);
    public static ApiException Conflict(string message) => new(StatusCodes.Status409Conflict, message);
}

/// <summary>
/// Turns exceptions into the { message } body the SPA shows. Unexpected errors never leak a stack
/// trace or SQL text to the browser; they are logged with a correlation id instead.
/// </summary>
public sealed class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ApiException ex) when (!context.Response.HasStarted)
        {
            await WriteAsync(context, ex.StatusCode, new { message = ex.Message });
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client went away; nothing to answer.
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            logger.LogError(ex, "Unhandled error {TraceId} on {Method} {Path}", context.TraceIdentifier, context.Request.Method, context.Request.Path);
            await WriteAsync(context, StatusCodes.Status500InternalServerError,
                new { message = "حدث خطأ غير متوقع. يرجى المحاولة لاحقاً.", traceId = context.TraceIdentifier });
        }
    }

    private static Task WriteAsync(HttpContext context, int status, object body)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsync(JsonSerializer.Serialize(body, ApiJson.Options));
    }
}

public static class ApiJson
{
    /// <summary>camelCase, like the MVC responses, for bodies written by middleware.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
