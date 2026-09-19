using ChatApp.Application.Common.Exceptions;
using System.Net;
using System.Text.Json;

namespace ChatApp.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (AppException ex)
        {
            logger.LogWarning(ex, "Application error {StatusCode}", ex.StatusCode);
            await WriteProblemAsync(context, ex.StatusCode, ex.Message, ex.GetType().Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error");
            await WriteProblemAsync(context, 500, "An unexpected error occurred.", "internal-error");
        }
    }

    private static Task WriteProblemAsync(HttpContext context, int status, string detail, string code)
    {
        // RFC 7807 style, per PLAN §29. Never leak stack traces / SQL errors.
        var traceId = context.TraceIdentifier;
        var title = status switch
        {
            400 => "Bad request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not found",
            409 => "Conflict",
            _ => "Internal server error"
        };
        var payload = new
        {
            type = $"https://chatapp.local/errors/{code.ToLowerInvariant()}",
            title,
            status,
            detail,
            traceId
        };
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
