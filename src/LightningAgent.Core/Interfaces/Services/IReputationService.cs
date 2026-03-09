using LightningAgent.Core.Enums;
using LightningAgent.Core.Models;

namespace LightningAgent.Core.Interfaces.Services;

public interface IReputationService
{
    Task<AgentReputation> UpdateReputationAsync(int agentId, bool taskCompleted, bool verificationPassed, double responseTimeSec, CancellationToken ct = default);
    Task<double> GetScoreAsync(int agentId, CancellationToken ct = default);
    Task<List<AgentReputation>> GetTopAgentsAsync(int count, SkillType? skillType = null, CancellationToken ct = default);
}
