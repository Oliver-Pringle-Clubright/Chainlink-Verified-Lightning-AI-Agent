using LightningAgent.AI.Prompts;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models.AI;
using Microsoft.Extensions.Logging;

namespace LightningAgent.AI.Orchestrator;

public class TaskDecomposer
{
    private readonly IClaudeAiClient _claude;
    private readonly ILogger<TaskDecomposer> _logger;

    public TaskDecomposer(IClaudeAiClient claude, ILogger<TaskDecomposer> logger)
    {
        _claude = claude;
        _logger = logger;
    }

    public async Task<OrchestrationPlan> DecomposeAsync(
        string title,
        string description,
        long budgetSats,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Decomposing task '{Title}' with budget {BudgetSats} sats",
            title,
            budgetSats);

        var userMessage = $"""
            ## Task to Decompose
            Title: {title}
            Description: {description}
            Total Budget: {budgetSats} satoshis

            Break this task into subtasks that can be independently assigned to specialized AI agents.
            Ensure the total estimated cost of all subtasks does not exceed the budget.
            """;

        var plan = await _claude.SendStructuredRequestAsync<OrchestrationPlan>(
            PromptTemplates.TaskDecomposition,
            userMessage,
            ct);

        _logger.LogInformation(
            "Task decomposed into {SubtaskCount} subtasks, estimated total: {TotalSats} sats",
            plan.Subtasks.Count,
            plan.EstimatedTotalSats);

        return plan;
    }
}
