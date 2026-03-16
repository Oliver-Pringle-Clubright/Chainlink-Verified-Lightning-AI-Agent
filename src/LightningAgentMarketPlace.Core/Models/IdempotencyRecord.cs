namespace LightningAgentMarketPlace.Core.Models;

/// <summary>
/// Represents a cached idempotency key entry for deduplicating mutating HTTP requests.
/// </summary>
public class IdempotencyRecord
{
    /// <summary>
    /// The unique idempotency key provided by the client.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// The HTTP method of the original request (POST, PUT, PATCH).
    /// </summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// The request path of the original request.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// The HTTP status code of the cached response.
    /// </summary>
    public int ResponseStatus { get; set; }

    /// <summary>
    /// The serialized response body.
    /// </summary>
    public string ResponseBody { get; set; } = string.Empty;

    /// <summary>
    /// When this idempotency record was created (UTC, ISO 8601).
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
