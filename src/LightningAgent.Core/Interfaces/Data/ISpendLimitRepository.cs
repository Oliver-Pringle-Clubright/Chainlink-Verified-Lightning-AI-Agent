using LightningAgent.Core.Models;

namespace LightningAgent.Core.Interfaces.Data;

public interface ISpendLimitRepository
{
    Task<SpendLimit?> GetByAgentIdAsync(int agentId, CancellationToken ct = default);
    Task<SpendLimit?> GetByTaskIdAsync(int taskId, CancellationToken ct = default);
    Task<int> CreateAsync(SpendLimit limit, CancellationToken ct = default);
    Task UpdateAsync(SpendLimit limit, CancellationToken ct = default);
    Task<IReadOnlyList<SpendLimit>> GetExpiredAsync(CancellationToken ct = default);
}
