using LightningAgent.Core.Models;

namespace LightningAgent.Core.Interfaces.Data;

public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<Payment>> GetByTaskIdAsync(int taskId, CancellationToken ct = default);
    Task<IReadOnlyList<Payment>> GetByAgentIdAsync(int agentId, CancellationToken ct = default);
    Task<int> GetCountAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Payment>> GetPagedAsync(int offset, int limit, CancellationToken ct = default);

    /// <summary>
    /// Returns the count of payments matching the optional filters.
    /// All filter parameters are combined with AND.
    /// </summary>
    Task<int> GetFilteredCountAsync(int? taskId = null, int? agentId = null, CancellationToken ct = default);

    /// <summary>
    /// Returns a page of payments matching the optional filters.
    /// Supports both offset and keyset (cursor-based) pagination.
    /// When <paramref name="cursor"/> is provided, keyset pagination is used (WHERE Id &lt; @cursor ORDER BY Id DESC);
    /// otherwise classic OFFSET/LIMIT is used.
    /// </summary>
    Task<IReadOnlyList<Payment>> GetFilteredPagedAsync(
        int offset,
        int limit,
        int? taskId = null,
        int? agentId = null,
        int? cursor = null,
        CancellationToken ct = default);

    Task<long> GetTotalSatsAsync(CancellationToken ct = default);
    Task<double> GetTotalUsdAsync(CancellationToken ct = default);
    Task<int> CreateAsync(Payment payment, CancellationToken ct = default);
    Task UpdateAsync(Payment payment, CancellationToken ct = default);
}
