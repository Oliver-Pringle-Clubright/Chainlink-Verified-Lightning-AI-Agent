using LightningAgent.Core.Models;

namespace LightningAgent.Core.Interfaces.Data;

public interface IDisputeRepository
{
    Task<Dispute?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<Dispute>> GetByTaskIdAsync(int taskId, CancellationToken ct = default);
    Task<IReadOnlyList<Dispute>> GetOpenAsync(CancellationToken ct = default);
    Task<int> CreateAsync(Dispute dispute, CancellationToken ct = default);
    Task UpdateAsync(Dispute dispute, CancellationToken ct = default);
}
