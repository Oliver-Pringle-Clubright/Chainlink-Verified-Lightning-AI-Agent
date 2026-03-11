using System.Collections.Concurrent;

namespace LightningAgent.Api.Middleware;

public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;

    private const int DefaultMaxRequestsPerMinute = 60;
    private const int AuthMaxRequestsPerMinute = 10;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

    private static readonly ConcurrentDictionary<string, SlidingWindow> Clients = new();
    private static DateTime _lastCleanup = DateTime.UtcNow;
    private static readonly object CleanupLock = new();

    private static readonly string[] SkipPaths =
    {
        "/api/health", "/openapi", "/scalar"
    };

    public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Skip rate limiting for health, openapi, and scalar paths
        if (SkipPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        // Check if this is an auth endpoint (always use IP-based rate limiting with a stricter limit)
        bool isAuthPath = path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase);

        // Determine rate limit key and limit
        string rateLimitKey;
        int limit;

        if (isAuthPath)
        {
            // Auth endpoints: always IP-based with aggressive limit.
            // Use a separate key prefix to keep auth and regular buckets independent.
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            rateLimitKey = $"auth-ip-{ip}";
            limit = AuthMaxRequestsPerMinute;
        }
        else if (context.Items.TryGetValue("AuthenticatedAgentId", out var agentIdObj) && agentIdObj is int agentId)
        {
            rateLimitKey = $"agent-{agentId}";
            limit = context.Items.TryGetValue("AuthenticatedAgentRateLimit", out var rlObj) && rlObj is int rl
                ? rl
                : DefaultMaxRequestsPerMinute;
        }
        else
        {
            rateLimitKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            limit = DefaultMaxRequestsPerMinute;
        }

        // Periodic cleanup of stale entries
        CleanupStaleEntries();

        var window = Clients.GetOrAdd(rateLimitKey, _ => new SlidingWindow());
        var count = window.CountInWindow(Window);

        if (count >= limit)
        {
            _logger.LogWarning(
                "Rate limit exceeded for client {ClientKey} (limit {Limit} req/min)",
                rateLimitKey, limit);

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "application/problem+json";
            context.Response.Headers["Retry-After"] = "60";

            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc6585#section-4",
                title = "Too Many Requests",
                status = 429,
                detail = $"Rate limit of {limit} requests per minute exceeded.",
                traceId = context.TraceIdentifier
            });
            return;
        }

        window.Record();

        await _next(context);
    }

    private static void CleanupStaleEntries()
    {
        var now = DateTime.UtcNow;
        if (now - _lastCleanup < CleanupInterval)
            return;

        lock (CleanupLock)
        {
            if (now - _lastCleanup < CleanupInterval)
                return;

            _lastCleanup = now;

            var staleKeys = Clients
                .Where(kvp => kvp.Value.IsStale(Window))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in staleKeys)
            {
                Clients.TryRemove(key, out _);
            }
        }
    }

    private class SlidingWindow
    {
        private readonly Queue<DateTime> _timestamps = new();
        private readonly object _lock = new();

        public int CountInWindow(TimeSpan window)
        {
            lock (_lock)
            {
                var cutoff = DateTime.UtcNow - window;
                while (_timestamps.Count > 0 && _timestamps.Peek() < cutoff)
                    _timestamps.Dequeue();
                return _timestamps.Count;
            }
        }

        public void Record()
        {
            lock (_lock)
            {
                _timestamps.Enqueue(DateTime.UtcNow);
            }
        }

        public bool IsStale(TimeSpan window)
        {
            lock (_lock)
            {
                var cutoff = DateTime.UtcNow - window;
                while (_timestamps.Count > 0 && _timestamps.Peek() < cutoff)
                    _timestamps.Dequeue();
                return _timestamps.Count == 0;
            }
        }
    }
}
