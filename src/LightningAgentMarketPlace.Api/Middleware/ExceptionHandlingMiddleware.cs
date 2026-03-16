using LightningAgentMarketPlace.Api.DTOs;

namespace LightningAgentMarketPlace.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var correlationId = context.Items.TryGetValue("CorrelationId", out var cid)
                ? cid?.ToString()
                : context.TraceIdentifier;

            var error = ex switch
            {
                InvalidOperationException ioe => CreateError(400, "Bad Request", ioe.Message, correlationId, context),
                ArgumentException ae => CreateError(400, "Bad Request", ae.Message, correlationId, context),
                KeyNotFoundException knf => CreateError(404, "Not Found", knf.Message, correlationId, context),
                UnauthorizedAccessException => CreateError(403, "Forbidden", "Access denied.", correlationId, context),
                OperationCanceledException => CreateError(499, "Client Closed Request", "The request was cancelled.", correlationId, context),
                _ => CreateError(500, "Internal Server Error", "An internal error occurred. Please try again later.", correlationId, context)
            };

            if (error.Status >= 500)
            {
                _logger.LogError(ex, "Unhandled exception occurred (correlationId={CorrelationId})", correlationId);
            }
            else
            {
                _logger.LogWarning(ex, "Request error {Status} (correlationId={CorrelationId}): {Detail}",
                    error.Status, correlationId, error.Detail);
            }

            context.Response.StatusCode = error.Status;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(error);
        }
    }

    private static ApiError CreateError(int status, string title, string detail, string? correlationId, HttpContext context)
    {
        var typeUrl = status switch
        {
            400 => "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            403 => "https://tools.ietf.org/html/rfc7231#section-6.5.3",
            404 => "https://tools.ietf.org/html/rfc7231#section-6.5.4",
            409 => "https://tools.ietf.org/html/rfc7231#section-6.5.8",
            429 => "https://tools.ietf.org/html/rfc6585#section-4",
            _ => "https://tools.ietf.org/html/rfc7231#section-6.6.1"
        };

        return new ApiError
        {
            Type = typeUrl,
            Title = title,
            Status = status,
            Detail = status >= 500 ? "An internal error occurred. Please try again later." : detail,
            CorrelationId = correlationId,
            TraceId = context.TraceIdentifier
        };
    }
}
