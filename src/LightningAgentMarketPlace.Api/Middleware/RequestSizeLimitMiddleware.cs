namespace LightningAgentMarketPlace.Api.Middleware;

public class RequestSizeLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly long _maxBodySize;

    public RequestSizeLimitMiddleware(RequestDelegate next, long maxBodySize = 10 * 1024 * 1024) // 10MB default
    {
        _next = next;
        _maxBodySize = maxBodySize;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.ContentLength > _maxBodySize)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.14",
                title = "Payload Too Large",
                status = 413,
                detail = $"Request body exceeds the maximum allowed size of {_maxBodySize / (1024 * 1024)}MB.",
                traceId = context.TraceIdentifier
            });
            return;
        }

        // Also set Kestrel's limit for streaming bodies
        var maxRequestBodySizeFeature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (maxRequestBodySizeFeature is not null && !maxRequestBodySizeFeature.IsReadOnly)
        {
            maxRequestBodySizeFeature.MaxRequestBodySize = _maxBodySize;
        }

        await _next(context);
    }
}
