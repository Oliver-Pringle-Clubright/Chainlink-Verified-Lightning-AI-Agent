using System.Collections.Concurrent;

namespace LightningAgent.Api.Middleware;

public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;

    private const int MaxRequestsPerMinute = 100;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

    private static readonly ConcurrentDictionary<string, ClientRequestInfo> Clients = new();
    private static DateTime _lastCleanup = DateTime.UtcNow;
    private static readonly object CleanupLock = new();

    public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Skip rate limiting for health endpoint
        if (path.StartsWith("/api/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Periodic cleanup of stale entries
        CleanupStaleEntries();

        var clientInfo = Clients.GetOrAdd(clientIp, _ => new ClientRequestInfo());

        lock (clientInfo)
        {
            var now = DateTime.UtcNow;

            // Remove timestamps outside the current window
            while (clientInfo.RequestTimestamps.Count > 0 &&
                   now - clientInfo.RequestTimestamps.Peek() > Window)
            {
                clientInfo.RequestTimestamps.Dequeue();
            }

            if (clientInfo.RequestTimestamps.Count >= MaxRequestsPerMinute)
            {
                _logger.LogWarning(
                    "Rate limit exceeded for client {ClientIp} ({Count} requests in window)",
                    clientIp, clientInfo.RequestTimestamps.Count);

                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.ContentType = "application/problem+json";
                context.Response.Headers["Retry-After"] = "60";

                // Write response synchronously inside lock, then return
                var problem = new
                {
                    type = "https://tools.ietf.org/html/rfc6585#section-4",
                    title = "Too Many Requests",
                    status = 429,
                    detail = $"Rate limit of {MaxRequestsPerMinute} requests per minute exceeded.",
                    traceId = context.TraceIdentifier
                };

                // We need to write outside the lock to avoid async-in-lock issues
                clientInfo.Blocked = true;
                clientInfo.BlockedProblem = problem;
                return;
            }

            clientInfo.RequestTimestamps.Enqueue(now);
            clientInfo.Blocked = false;
        }

        if (clientInfo.Blocked && clientInfo.BlockedProblem is not null)
        {
            await context.Response.WriteAsJsonAsync(clientInfo.BlockedProblem);
            return;
        }

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
                .Where(kvp =>
                {
                    lock (kvp.Value)
                    {
                        return kvp.Value.RequestTimestamps.Count == 0 ||
                               now - kvp.Value.RequestTimestamps.Peek() > Window;
                    }
                })
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in staleKeys)
            {
                Clients.TryRemove(key, out _);
            }
        }
    }

    private sealed class ClientRequestInfo
    {
        public Queue<DateTime> RequestTimestamps { get; } = new();
        public bool Blocked { get; set; }
        public object? BlockedProblem { get; set; }
    }
}
