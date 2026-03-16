using LightningAgentMarketPlace.Core.Enums;
using LightningAgentMarketPlace.Core.Models;

namespace LightningAgentMarketPlace.Core.Interfaces.Data;

public interface IAgentCapabilityRepository
{
    Task<IReadOnlyList<AgentCapability>> GetByAgentIdAsync(int agentId, CancellationToken ct = default);
    Task<IReadOnlyList<AgentCapability>> GetBySkillTypeAsync(SkillType skillType, CancellationToken ct = default);
    Task<int> CreateAsync(AgentCapability capability, CancellationToken ct = default);
    Task DeleteByAgentIdAsync(int agentId, CancellationToken ct = default);
}
