using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace LightningAgent.Engine;

public class ReputationService : IReputationService
{
    private readonly IAgentReputationRepository _reputationRepo;
    private readonly IAgentRepository _agentRepo;
    private readonly IAgentCapabilityRepository _capabilityRepo;
    private readonly ICachedDataService _cachedData;
    private readonly ILogger<ReputationService> _logger;

    public ReputationService(
        IAgentReputationRepository reputationRepo,
        IAgentRepository agentRepo,
        IAgentCapabilityRepository capabilityRepo,
        ICachedDataService cachedData,
        ILogger<ReputationService> logger)
    {
        _reputationRepo = reputationRepo;
        _agentRepo = agentRepo;
        _capabilityRepo = capabilityRepo;
        _cachedData = cachedData;
        _logger = logger;
    }

    public async Task<AgentReputation> UpdateReputationAsync(
        int agentId,
        bool taskCompleted,
        bool verificationPassed,
        double responseTimeSec,
        CancellationToken ct = default)
    {
        var reputation = await _reputationRepo.GetByAgentIdAsync(agentId, ct);

        if (reputation is null)
        {
            _logger.LogInformation("Creating new reputation record for agent {AgentId}", agentId);
            reputation = new AgentReputation
            {
                AgentId = agentId,
                TotalTasks = 0,
                CompletedTasks = 0,
                VerificationPasses = 0,
                VerificationFails = 0,
                DisputeCount = 0,
                AvgResponseTimeSec = 0.0,
                ReputationScore = 0.5,
                LastUpdated = DateTime.UtcNow
            };
            reputation.Id = await _reputationRepo.CreateAsync(reputation, ct);
        }

        // 1. Increment TotalTasks
        reputation.TotalTasks++;

        // 2. If taskCompleted, increment CompletedTasks
        if (taskCompleted)
        {
            reputation.CompletedTasks++;
        }

        // 3. Update verification counters
        if (verificationPassed)
        {
            reputation.VerificationPasses++;
        }
        else
        {
            reputation.VerificationFails++;
        }

        // 4. Update AvgResponseTimeSec using rolling average
        if (reputation.TotalTasks == 1)
        {
            reputation.AvgResponseTimeSec = responseTimeSec;
        }
        else
        {
            reputation.AvgResponseTimeSec =
                ((reputation.AvgResponseTimeSec * (reputation.TotalTasks - 1)) + responseTimeSec)
                / reputation.TotalTasks;
        }

        // 5. Calculate ReputationScore using weighted formula
        double completionRate = (double)reputation.CompletedTasks / reputation.TotalTasks;

        int totalVerifications = reputation.VerificationPasses + reputation.VerificationFails;
        double verificationRate = totalVerifications > 0
            ? (double)reputation.VerificationPasses / totalVerifications
            : 0.5;

        double disputePenalty = Math.Clamp(1.0 - (reputation.DisputeCount * 0.1), 0.0, 1.0);

        double speedBonus = Math.Clamp(Math.Max(0, 1.0 - (reputation.AvgResponseTimeSec / 3600.0)), 0.0, 1.0);

        double score = (0.3 * completionRate)
                     + (0.4 * verificationRate)
                     + (0.2 * disputePenalty)
                     + (0.1 * speedBonus);

        reputation.ReputationScore = Math.Clamp(score, 0.0, 1.0);
        reputation.LastUpdated = DateTime.UtcNow;

        await _reputationRepo.UpdateAsync(reputation, ct);
        _cachedData.InvalidateAgent(agentId);

        _logger.LogInformation(
            "Updated reputation for agent {AgentId}: Score={Score:F3}, TotalTasks={TotalTasks}",
            agentId, reputation.ReputationScore, reputation.TotalTasks);

        return reputation;
    }

    public async Task<double> GetScoreAsync(int agentId, CancellationToken ct = default)
    {
        var reputation = await _reputationRepo.GetByAgentIdAsync(agentId, ct);
        return reputation?.ReputationScore ?? 0.5;
    }

    public async Task<List<AgentReputation>> GetTopAgentsAsync(
        int count,
        SkillType? skillType = null,
        CancellationToken ct = default)
    {
        List<AgentReputation> reputations;

        if (skillType.HasValue)
        {
            // Get agents that have the requested capability
            var capabilities = await _capabilityRepo.GetBySkillTypeAsync(skillType.Value, ct);
            var agentIds = capabilities.Select(c => c.AgentId).Distinct().ToList();

            var reputationTasks = agentIds.Select(id => _reputationRepo.GetByAgentIdAsync(id, ct));
            var results = await Task.WhenAll(reputationTasks);
            reputations = results.Where(r => r is not null).Cast<AgentReputation>().ToList();
        }
        else
        {
            // Get all active agents
            var agents = await _agentRepo.GetAllAsync(AgentStatus.Active, ct);

            var reputationTasks = agents.Select(a => _reputationRepo.GetByAgentIdAsync(a.Id, ct));
            var results = await Task.WhenAll(reputationTasks);
            reputations = results.Where(r => r is not null).Cast<AgentReputation>().ToList();
        }

        return reputations
            .OrderByDescending(r => r.ReputationScore)
            .Take(count)
            .ToList();
    }
}
