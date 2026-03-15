using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using TaskStatus = LightningAgent.Core.Enums.TaskStatus;

namespace LightningAgent.Api.Controllers;

/// <summary>
/// Provides portfolio summaries of agent completed work.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/agents")]
[Route("api/v{version:apiVersion}/agents")]
[Produces("application/json")]
public class PortfoliosController : ControllerBase
{
    private readonly ITaskRepository _taskRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly IAgentReputationRepository _reputationRepository;
    private readonly ILogger<PortfoliosController> _logger;

    public PortfoliosController(
        ITaskRepository taskRepository,
        IAgentRepository agentRepository,
        IAgentReputationRepository reputationRepository,
        ILogger<PortfoliosController> logger)
    {
        _taskRepository = taskRepository;
        _agentRepository = agentRepository;
        _reputationRepository = reputationRepository;
        _logger = logger;
    }

    /// <summary>
    /// Get an agent's portfolio: completed work summary, reputation, and earnings.
    /// </summary>
    [HttpGet("{id:int}/portfolio")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPortfolio(int id, CancellationToken ct)
    {
        var agent = await _agentRepository.GetByIdAsync(id, ct);
        if (agent is null)
            return NotFound($"Agent {id} not found.");

        // Load all tasks assigned to this agent and filter to Completed
        var allTasks = await _taskRepository.GetByAssignedAgentAsync(id, ct);
        var completedTasks = allTasks
            .Where(t => t.Status == TaskStatus.Completed)
            .ToList();

        // Load reputation
        var reputation = await _reputationRepository.GetByAgentIdAsync(id, ct);
        var reputationScore = reputation?.ReputationScore ?? 0.0;

        // Calculate totals
        var totalEarned = completedTasks.Sum(t => t.ActualPayoutSats);
        var verificationScores = completedTasks
            .Where(t => t.PriceUsd.HasValue)
            .Select(t => t.PriceUsd!.Value)
            .ToList();
        var averageVerificationScore = verificationScores.Count > 0
            ? Math.Round(verificationScores.Average(), 4)
            : 0.0;

        var taskSummaries = completedTasks.Select(t => new
        {
            taskId = t.Id,
            title = t.Title,
            taskType = t.TaskType.ToString(),
            completedAt = t.CompletedAt,
            payoutSats = t.ActualPayoutSats
        }).ToList();

        return Ok(new
        {
            agentId = id,
            agentName = agent.Name,
            reputationScore,
            completedTasks = taskSummaries,
            totalEarned,
            averageVerificationScore
        });
    }
}
