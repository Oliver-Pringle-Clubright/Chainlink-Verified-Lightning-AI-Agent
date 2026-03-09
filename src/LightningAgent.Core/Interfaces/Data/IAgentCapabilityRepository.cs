using LightningAgent.Core.Enums;
using LightningAgent.Core.Models;

namespace LightningAgent.Core.Interfaces.Data;

public interface IAgentCapabilityRepository
{
    Task<IReadOnlyList<AgentCapability>> GetByAgentIdAsync(int agentId, CancellationToken ct = default);
    Task<IReadOnlyList<AgentCapability>> GetBySkillTypeAsync(SkillType skillType, CancellationToken ct = default);
    Task<int> CreateAsync(AgentCapability capability, CancellationToken ct = default);
    Task DeleteByAgentIdAsync(int agentId, CancellationToken ct = default);
}
