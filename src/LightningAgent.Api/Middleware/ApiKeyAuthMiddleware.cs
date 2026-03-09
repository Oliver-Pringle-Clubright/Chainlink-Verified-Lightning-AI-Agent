namespace LightningAgent.Api.Middleware;

public class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApiKeyAuthMiddleware> _logger;

    private const string ApiKeyHeader = "X-Api-Key";

    private static readonly string[] SkipPaths = { "/api/health", "/swagger" };

    public ApiKeyAuthMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<ApiKeyAuthMiddleware> logger)
    {
        _next = next;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Skip auth for health and swagger endpoints
        if (SkipPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var configuredKey = _configuration["ApiSecurity:ApiKey"];

        // Dev mode: if no API key is configured, allow all requests
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var providedKey) ||
            !string.Equals(providedKey, configuredKey, StringComparison.Ordinal))
        {
            _logger.LogWarning("Unauthorized request to {Path} - invalid or missing API key", path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc7235#section-3.1",
                title = "Unauthorized",
                status = 401,
                detail = "Invalid or missing API key.",
                traceId = context.TraceIdentifier
            });
            return;
        }

        await _next(context);
    }
}
