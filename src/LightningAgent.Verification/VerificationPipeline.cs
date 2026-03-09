using System.Text.Json;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace LightningAgent.Verification;

public class VerificationPipeline : IVerificationPipeline
{
    private readonly IEnumerable<IVerificationStrategy> _strategies;
    private readonly ILogger<VerificationPipeline> _logger;

    public VerificationPipeline(
        IEnumerable<IVerificationStrategy> strategies,
        ILogger<VerificationPipeline> logger)
    {
        _strategies = strategies;
        _logger = logger;
    }

    public async Task<List<VerificationResult>> RunVerificationAsync(
        Milestone milestone,
        byte[] agentOutput,
        CancellationToken ct = default)
    {
        var taskType = ParseTaskType(milestone.VerificationCriteria);

        _logger.LogInformation(
            "Running verification pipeline for milestone {MilestoneId} with task type {TaskType}",
            milestone.Id,
            taskType);

        var applicableStrategies = _strategies
            .Where(s => s.CanHandle(taskType))
            .ToList();

        _logger.LogInformation(
            "Found {Count} applicable verification strategies: {Strategies}",
            applicableStrategies.Count,
            string.Join(", ", applicableStrategies.Select(s => s.StrategyType)));

        var tasks = applicableStrategies
            .Select(strategy => RunStrategyAsync(strategy, milestone, agentOutput, ct))
            .ToList();

        var results = await Task.WhenAll(tasks);

        foreach (var result in results)
        {
            _logger.LogInformation(
                "Verification result [{Strategy}]: Score={Score}, Passed={Passed}, Details={Details}",
                result.StrategyType,
                result.Score,
                result.Passed,
                result.Details);
        }

        return results.ToList();
    }

    private async Task<VerificationResult> RunStrategyAsync(
        IVerificationStrategy strategy,
        Milestone milestone,
        byte[] agentOutput,
        CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("Running verification strategy: {Strategy}", strategy.StrategyType);
            return await strategy.VerifyAsync(milestone, agentOutput, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Verification strategy {Strategy} failed for milestone {MilestoneId}",
                strategy.StrategyType,
                milestone.Id);

            return new VerificationResult(
                Score: 0.0,
                Passed: false,
                Details: $"Strategy {strategy.StrategyType} failed: {ex.Message}",
                StrategyType: strategy.StrategyType);
        }
    }

    private TaskType ParseTaskType(string verificationCriteria)
    {
        if (string.IsNullOrWhiteSpace(verificationCriteria))
        {
            return TaskType.Text;
        }

        try
        {
            using var doc = JsonDocument.Parse(verificationCriteria);
            if (doc.RootElement.TryGetProperty("taskType", out var taskTypeElement))
            {
                var taskTypeStr = taskTypeElement.GetString();
                if (Enum.TryParse<TaskType>(taskTypeStr, ignoreCase: true, out var taskType))
                {
                    return taskType;
                }
            }
        }
        catch (JsonException)
        {
            _logger.LogDebug(
                "VerificationCriteria is not valid JSON, defaulting to Text task type");
        }

        return TaskType.Text;
    }
}
