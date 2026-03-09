using LightningAgent.Core.Models;

namespace LightningAgent.Core.Interfaces.Services;

public interface IAgentMatcher
{
    Task<List<(Agent Agent, double MatchScore)>> FindBestAgentAsync(TaskItem task, CancellationToken ct = default);
}
