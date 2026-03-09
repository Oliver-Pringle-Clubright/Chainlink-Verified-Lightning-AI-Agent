namespace LightningAgent.Api.Middleware;

public class CorrelationIdMiddleware
{
    private const string CorrelationIdHeader = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[CorrelationIdHeader].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N");

        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers[CorrelationIdHeader] = correlationId;

        using var scope = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CorrelationId")
            .BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });

        await _next(context);
    }
}
