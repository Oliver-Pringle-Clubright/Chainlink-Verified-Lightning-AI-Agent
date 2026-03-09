using LightningAgent.Core.Enums;
using LightningAgent.Core.Models;

namespace LightningAgent.Core.Interfaces.Data;

public interface IAgentRepository
{
    Task<Agent?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Agent?> GetByExternalIdAsync(string externalId, CancellationToken ct = default);
    Task<IReadOnlyList<Agent>> GetAllAsync(AgentStatus? status = null, CancellationToken ct = default);
    Task<int> GetCountAsync(AgentStatus? status = null, CancellationToken ct = default);
    Task<IReadOnlyList<Agent>> GetPagedAsync(int offset, int limit, AgentStatus? status = null, CancellationToken ct = default);
    Task<int> CreateAsync(Agent agent, CancellationToken ct = default);
    Task UpdateAsync(Agent agent, CancellationToken ct = default);
    Task UpdateStatusAsync(int id, AgentStatus status, CancellationToken ct = default);
    Task<Agent?> GetByApiKeyHashAsync(string apiKeyHash, CancellationToken ct = default);
}
