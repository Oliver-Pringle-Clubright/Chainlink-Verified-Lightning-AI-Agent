namespace LightningAgent.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IWebHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IWebHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
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

            // In development, include the full exception message for debugging.
            // In production/staging, return a generic message to avoid leaking internal details.
            var detail = _environment.IsDevelopment()
                ? ex.Message
                : "An internal error occurred. Please try again later or contact support.";

            var problem = new
            {
                type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                title = "Internal Server Error",
                status = 500,
                detail,
                traceId = context.TraceIdentifier
            };
            await context.Response.WriteAsJsonAsync(problem);
        }
    }
}
