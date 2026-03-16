using LightningAgentMarketPlace.Core.Enums;
using LightningAgentMarketPlace.Core.Models;

namespace LightningAgentMarketPlace.Core.Interfaces.Data;

public interface IMilestoneRepository
{
    Task<Milestone?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<Milestone>> GetByTaskIdAsync(int taskId, CancellationToken ct = default);
    Task<int> CreateAsync(Milestone milestone, CancellationToken ct = default);
    Task UpdateAsync(Milestone milestone, CancellationToken ct = default);
    Task UpdateStatusAsync(int id, MilestoneStatus status, CancellationToken ct = default);
    Task<IReadOnlyList<Milestone>> GetCompletedByAgentAsync(int agentId, int limit = 10, CancellationToken ct = default);
}
