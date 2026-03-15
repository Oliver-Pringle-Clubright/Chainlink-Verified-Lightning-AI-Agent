namespace LightningAgent.Api.Middleware;

using System.Security.Cryptography;
using LightningAgent.Api.Helpers;
using LightningAgent.Core.Interfaces.Data;

public class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApiKeyAuthMiddleware> _logger;

    private const string ApiKeyHeader = "X-Api-Key";

    private static readonly string[] SkipPaths = { "/api/health", "/api/auth", "/swagger", "/scalar", "/openapi" };

    // Static files (dashboard, JS, CSS) bypass API key auth — the dashboard
    // itself handles authentication via the login UI and X-Api-Key header on API calls
    private static readonly string[] StaticFileExtensions = { ".html", ".js", ".css", ".ico", ".png", ".svg" };

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

        // Skip auth for health, swagger, and static file endpoints
        if (SkipPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            || StaticFileExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var configuredKey = _configuration["ApiSecurity:ApiKey"];

        // Dev mode must be explicitly enabled; empty API key alone is not enough
        var devModeEnabled = string.Equals(
            _configuration["ApiSecurity:DevMode"], "true", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            if (devModeEnabled)
            {
                context.Items["DevMode"] = true;
                await _next(context);
                return;
            }

            // No API key configured and dev mode not enabled - reject all requests
            _logger.LogError("No API key configured (ApiSecurity:ApiKey) and DevMode is not enabled. " +
                "Set ApiSecurity:ApiKey or set ApiSecurity:DevMode=true for development.");
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.6.4",
                title = "Service Unavailable",
                status = 503,
                detail = "API authentication is not configured. Contact the administrator.",
                traceId = context.TraceIdentifier
            });
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var providedKey) ||
            string.IsNullOrEmpty(providedKey))
        {
            await WriteUnauthorized(context, path);
            return;
        }

        // 1. Check for global API key (admin access) — supports comma-separated list
        var configuredKeys = configuredKey.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var providedBytes = System.Text.Encoding.UTF8.GetBytes(providedKey!);
        bool isAdminKey = false;
        foreach (var key in configuredKeys)
        {
            if (CryptographicOperations.FixedTimeEquals(providedBytes, System.Text.Encoding.UTF8.GetBytes(key)))
            {
                isAdminKey = true;
                break;
            }
        }
        if (isAdminKey)
        {
            context.Items["IsAdmin"] = true;
            await _next(context);
            return;
        }

        // 2. Check for per-agent API key (salted hash requires checking each agent)
        var agentRepo = context.RequestServices.GetRequiredService<IAgentRepository>();
        var agent = await agentRepo.GetByApiKeyAsync(providedKey!, context.RequestAborted);

        if (agent is not null)
        {
            context.Items["AuthenticatedAgentId"] = agent.Id;
            context.Items["AuthenticatedAgentRateLimit"] = agent.RateLimitPerMinute;
            await _next(context);
            return;
        }

        // 3. No match at all
        await WriteUnauthorized(context, path);
    }

    private async Task WriteUnauthorized(HttpContext context, string path)
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
    }
}
