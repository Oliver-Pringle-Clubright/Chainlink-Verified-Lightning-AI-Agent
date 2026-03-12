namespace LightningAgent.Api.DTOs;

/// <summary>
/// Standardized error response returned by all API endpoints.
/// Follows RFC 7807 (Problem Details for HTTP APIs).
/// </summary>
public class ApiError
{
    public string Type { get; set; } = "about:blank";
    public string Title { get; set; } = "Error";
    public int Status { get; set; }
    public string Detail { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
    public string? TraceId { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }

    public static ApiError BadRequest(string detail, Dictionary<string, string[]>? errors = null) => new()
    {
        Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
        Title = "Bad Request",
        Status = 400,
        Detail = detail,
        Errors = errors
    };

    public static ApiError NotFound(string detail) => new()
    {
        Type = "https://tools.ietf.org/html/rfc7231#section-6.5.4",
        Title = "Not Found",
        Status = 404,
        Detail = detail
    };

    public static ApiError Conflict(string detail) => new()
    {
        Type = "https://tools.ietf.org/html/rfc7231#section-6.5.8",
        Title = "Conflict",
        Status = 409,
        Detail = detail
    };

    public static ApiError Forbidden(string detail = "Access denied.") => new()
    {
        Type = "https://tools.ietf.org/html/rfc7231#section-6.5.3",
        Title = "Forbidden",
        Status = 403,
        Detail = detail
    };

    public static ApiError TooManyRequests(string detail, int retryAfterSeconds = 60) => new()
    {
        Type = "https://tools.ietf.org/html/rfc6585#section-4",
        Title = "Too Many Requests",
        Status = 429,
        Detail = detail
    };

    public static ApiError InternalError(string? correlationId = null, string? traceId = null) => new()
    {
        Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
        Title = "Internal Server Error",
        Status = 500,
        Detail = "An internal error occurred. Please try again later.",
        CorrelationId = correlationId,
        TraceId = traceId
    };
}
