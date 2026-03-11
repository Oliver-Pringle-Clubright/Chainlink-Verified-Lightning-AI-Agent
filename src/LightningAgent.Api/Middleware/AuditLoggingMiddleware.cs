using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;

namespace LightningAgent.Api.Middleware;

public class AuditLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditLoggingMiddleware> _logger;

    private static readonly string[] SkipPrefixes =
    {
        "/api/health",
        "/swagger",
        "/scalar",
        "/openapi",
        "/hubs/",
        "/_framework",
        "/favicon.ico"
    };

    public AuditLoggingMiddleware(RequestDelegate next, ILogger<AuditLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Skip health check, swagger, static file, and SignalR requests
        if (ShouldSkip(path))
        {
            await _next(context);
            return;
        }

        // Capture request details before calling next
        var method = context.Request.Method;
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = context.Request.Headers.UserAgent.ToString();
        var agentId = GetAgentId(context);

        await _next(context);

        // Log after the response has been generated
        var statusCode = context.Response.StatusCode;

        try
        {
            var auditRepo = context.RequestServices.GetService<IAuditLogRepository>();
            if (auditRepo is not null)
            {
                var sanitizedPath = SanitizePath(path);
                var entry = new AuditLogEntry
                {
                    EventType = $"{method} {statusCode}",
                    EntityType = "HttpRequest",
                    EntityId = agentId ?? 0,
                    Details = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        path = sanitizedPath,
                        method,
                        statusCode,
                        ipAddress,
                        userAgent
                    }),
                    CreatedAt = DateTime.UtcNow
                };

                await auditRepo.CreateAsync(entry);
            }
        }
        catch (Exception ex)
        {
            // Never let audit logging failures break the request pipeline
            _logger.LogWarning(ex, "Failed to write audit log entry for {Method} {Path}", method, SanitizePath(path));
        }
    }

    private static string SanitizePath(string path)
    {
        // Remove query string to prevent logging sensitive parameters
        var queryIndex = path.IndexOf('?');
        if (queryIndex >= 0)
        {
            var basePath = path[..queryIndex];
            var query = path[queryIndex..];
            // Redact known sensitive params
            query = System.Text.RegularExpressions.Regex.Replace(
                query,
                @"((?:api[_-]?key|token|secret|password|authorization|key)=)[^&]*",
                "$1[REDACTED]",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return basePath + query;
        }
        return path;
    }

    private static bool ShouldSkip(string path)
    {
        foreach (var prefix in SkipPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static int? GetAgentId(HttpContext context)
    {
        if (context.Items.TryGetValue("AuthenticatedAgentId", out var id) && id is int agentId)
            return agentId;
        return null;
    }
}
