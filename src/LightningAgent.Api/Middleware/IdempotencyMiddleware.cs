using LightningAgent.Core.Interfaces.Data;

namespace LightningAgent.Api.Middleware;

/// <summary>
/// Middleware that provides idempotency for mutating HTTP requests (POST, PUT, PATCH).
/// When a client sends an <c>Idempotency-Key</c> header, the middleware checks if the
/// key has been seen before (within 24 hours). If so, the cached response is returned.
/// If not, the request is executed normally and the response is cached for future replay.
/// </summary>
public class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<IdempotencyMiddleware> _logger;

    private static readonly HashSet<string> MutatingMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PUT", "PATCH"
    };

    public IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only intercept mutating methods
        if (!MutatingMethods.Contains(context.Request.Method))
        {
            await _next(context);
            return;
        }

        // Check for Idempotency-Key header
        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var keyValues) ||
            string.IsNullOrWhiteSpace(keyValues.FirstOrDefault()))
        {
            await _next(context);
            return;
        }

        var idempotencyKey = keyValues.First()!;
        var idempotencyRepo = context.RequestServices.GetService<IIdempotencyRepository>();

        if (idempotencyRepo is null)
        {
            // If the repository isn't registered, just pass through
            await _next(context);
            return;
        }

        // Check if we have a cached response for this key
        var existing = await idempotencyRepo.GetAsync(idempotencyKey, context.RequestAborted);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Returning cached idempotent response for key {IdempotencyKey} (status {Status})",
                idempotencyKey, existing.ResponseStatus);

            context.Response.StatusCode = existing.ResponseStatus;
            context.Response.ContentType = "application/json";
            context.Response.Headers["Idempotency-Key"] = idempotencyKey;
            context.Response.Headers["X-Idempotent-Replayed"] = "true";

            if (!string.IsNullOrEmpty(existing.ResponseBody))
            {
                await context.Response.WriteAsync(existing.ResponseBody, context.RequestAborted);
            }
            return;
        }

        // Capture the response by replacing the body stream
        var originalBodyStream = context.Response.Body;
        using var capturedBody = new MemoryStream();
        context.Response.Body = capturedBody;

        try
        {
            await _next(context);

            // Read the captured response body
            capturedBody.Seek(0, SeekOrigin.Begin);
            var responseBody = await new StreamReader(capturedBody).ReadToEndAsync(context.RequestAborted);

            // Cache the response
            var path = context.Request.Path.Value ?? string.Empty;
            var method = context.Request.Method;

            try
            {
                await idempotencyRepo.SaveAsync(
                    idempotencyKey, method, path,
                    context.Response.StatusCode, responseBody,
                    context.RequestAborted);

                _logger.LogDebug(
                    "Cached idempotent response for key {IdempotencyKey}: {Method} {Path} -> {Status}",
                    idempotencyKey, method, path, context.Response.StatusCode);
            }
            catch (Exception ex)
            {
                // Don't fail the request if we can't cache the response
                _logger.LogWarning(ex, "Failed to cache idempotent response for key {IdempotencyKey}", idempotencyKey);
            }

            // Write the captured body to the original stream
            capturedBody.Seek(0, SeekOrigin.Begin);
            await capturedBody.CopyToAsync(originalBodyStream, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = originalBodyStream;
        }
    }
}
