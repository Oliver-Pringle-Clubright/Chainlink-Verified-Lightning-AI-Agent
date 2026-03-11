using LightningAgent.Core.Models;

namespace LightningAgent.Core.Interfaces.Data;

/// <summary>
/// Repository for managing idempotency key records used to deduplicate mutating HTTP requests.
/// </summary>
public interface IIdempotencyRepository
{
    /// <summary>
    /// Retrieves a cached idempotency record by its key, or null if not found.
    /// </summary>
    Task<IdempotencyRecord?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Saves a new idempotency record. If the key already exists, this is a no-op.
    /// </summary>
    Task SaveAsync(string key, string method, string path, int status, string body, CancellationToken ct = default);

    /// <summary>
    /// Deletes all idempotency records older than the specified cutoff date.
    /// Returns the number of rows deleted.
    /// </summary>
    Task<int> CleanupOlderThanAsync(DateTime cutoff, CancellationToken ct = default);
}
