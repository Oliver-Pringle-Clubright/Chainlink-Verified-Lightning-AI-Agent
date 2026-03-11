namespace LightningAgent.Api.Middleware;

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
            _logger.LogError(ex, "Unhandled exception occurred");
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/problem+json";

            // Always return generic error to client - never leak exception details
            var detail = "An internal error occurred. Please try again later.";
            // Correlation ID helps support staff find the detailed logs
            var correlationId = context.Items.TryGetValue("CorrelationId", out var cid) ? cid?.ToString() : context.TraceIdentifier;

            var problem = new
            {
                type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                title = "Internal Server Error",
                status = 500,
                detail,
                correlationId,
                traceId = context.TraceIdentifier
            };
            await context.Response.WriteAsJsonAsync(problem);
        }
    }
}
