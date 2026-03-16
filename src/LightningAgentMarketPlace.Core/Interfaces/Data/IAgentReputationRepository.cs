using LightningAgentMarketPlace.Core.Models;

namespace LightningAgentMarketPlace.Core.Interfaces.Data;

public interface IAgentReputationRepository
{
    Task<AgentReputation?> GetByAgentIdAsync(int agentId, CancellationToken ct = default);
    Task<int> CreateAsync(AgentReputation reputation, CancellationToken ct = default);
    Task UpdateAsync(AgentReputation reputation, CancellationToken ct = default);
}
