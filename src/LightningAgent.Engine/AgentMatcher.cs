using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace LightningAgent.Engine;

public class AgentMatcher : IAgentMatcher
{
    private readonly IAgentRepository _agentRepo;
    private readonly IAgentCapabilityRepository _capabilityRepo;
    private readonly IAgentReputationRepository _reputationRepo;
    private readonly ILogger<AgentMatcher> _logger;

    public AgentMatcher(
        IAgentRepository agentRepo,
        IAgentCapabilityRepository capabilityRepo,
        IAgentReputationRepository reputationRepo,
        ILogger<AgentMatcher> logger)
    {
        _agentRepo = agentRepo;
        _capabilityRepo = capabilityRepo;
        _reputationRepo = reputationRepo;
        _logger = logger;
    }

    public async Task<List<(Agent Agent, double MatchScore)>> FindBestAgentAsync(
        TaskItem task,
        CancellationToken ct = default)
    {
        var requiredSkill = MapTaskTypeToSkillType(task.TaskType);

        _logger.LogInformation(
            "Finding agents for task {TaskId} (type={TaskType}, skill={SkillType}, budget={MaxPayout} sats)",
            task.Id, task.TaskType, requiredSkill, task.MaxPayoutSats);

        // Get all active agents
        var activeAgents = await _agentRepo.GetAllAsync(AgentStatus.Active, ct);

        var scoredAgents = new List<(Agent Agent, double MatchScore)>();

        foreach (var agent in activeAgents)
        {
            // Get capabilities for this agent
            var capabilities = await _capabilityRepo.GetByAgentIdAsync(agent.Id, ct);

            // Filter to agents that have the required skill
            var matchingCapability = capabilities.FirstOrDefault(c => c.SkillType == requiredSkill);
            if (matchingCapability is null)
            {
                continue;
            }

            // Get reputation (default 0.5 if none)
            var reputation = await _reputationRepo.GetByAgentIdAsync(agent.Id, ct);
            double reputationScore = reputation?.ReputationScore ?? 0.5;

            // Calculate match score components
            double reputationWeight = reputationScore * 0.5;

            // Price weight: lower price relative to budget = higher score
            double priceWeight = 0.0;
            if (task.MaxPayoutSats > 0 && matchingCapability.PriceSatsPerUnit > 0)
            {
                double priceRatio = (double)matchingCapability.PriceSatsPerUnit / task.MaxPayoutSats;
                priceWeight = Math.Clamp(1.0 - priceRatio, 0.0, 1.0) * 0.3;
            }
            else if (matchingCapability.PriceSatsPerUnit == 0)
            {
                // Free agent gets max price score
                priceWeight = 0.3;
            }

            // Capacity weight: bonus if agent has remaining concurrency
            double capacityWeight = matchingCapability.MaxConcurrency > 0 ? 0.2 : 0.0;

            double matchScore = reputationWeight + priceWeight + capacityWeight;

            scoredAgents.Add((agent, matchScore));
        }

        // Sort by match score descending
        scoredAgents.Sort((a, b) => b.MatchScore.CompareTo(a.MatchScore));

        _logger.LogInformation(
            "Found {Count} matching agents for task {TaskId}",
            scoredAgents.Count, task.Id);

        return scoredAgents;
    }

    private static SkillType MapTaskTypeToSkillType(TaskType taskType) => taskType switch
    {
        TaskType.Code => SkillType.CodeGeneration,
        TaskType.Data => SkillType.DataAnalysis,
        TaskType.Text => SkillType.TextWriting,
        TaskType.Image => SkillType.ImageGeneration,
        _ => throw new ArgumentOutOfRangeException(nameof(taskType), taskType, "Unknown TaskType")
    };
}
