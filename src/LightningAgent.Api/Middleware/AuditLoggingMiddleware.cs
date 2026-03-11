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
                var entry = new AuditLogEntry
                {
                    EventType = $"{method} {statusCode}",
                    EntityType = "HttpRequest",
                    EntityId = agentId ?? 0,
                    Details = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        path,
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
            _logger.LogWarning(ex, "Failed to write audit log entry for {Method} {Path}", method, path);
        }
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
