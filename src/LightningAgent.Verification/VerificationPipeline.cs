using System.Text.Json;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace LightningAgent.Verification;

public class VerificationPipeline : IVerificationPipeline
{
    private readonly IEnumerable<IVerificationStrategy> _strategies;
    private readonly IVerificationStrategyConfigRepository _configRepo;
    private readonly ILogger<VerificationPipeline> _logger;

    public VerificationPipeline(
        IEnumerable<IVerificationStrategy> strategies,
        IVerificationStrategyConfigRepository configRepo,
        ILogger<VerificationPipeline> logger)
    {
        _strategies = strategies;
        _configRepo = configRepo;
        _logger = logger;
    }

    public async Task<VerificationPipelineResult> RunVerificationAsync(
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

        // Apply learned strategy weights to produce weighted scores
        var weightedResults = new List<VerificationResult>(results.Length);
        double totalWeight = 0.0;
        double weightedScoreSum = 0.0;

        foreach (var result in results)
        {
            double weight = 1.0; // default weight

            try
            {
                var strategyParams = await _configRepo.GetByStrategyTypeAsync(result.StrategyType, ct);
                var weightParam = strategyParams.FirstOrDefault(p => p.ParameterName == "Weight");
                if (weightParam is not null && double.TryParse(weightParam.ParameterValue, out var parsedWeight))
                {
                    weight = parsedWeight;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to load weight config for strategy {Strategy}, using default weight 1.0",
                    result.StrategyType);
            }

            totalWeight += weight;
            weightedScoreSum += result.Score * weight;
            weightedResults.Add(result);
        }

        var weightedAverage = totalWeight > 0 ? weightedScoreSum / totalWeight : 0.0;

        _logger.LogInformation(
            "Weighted verification score for milestone {MilestoneId}: {WeightedScore:F3} (totalWeight={TotalWeight:F2})",
            milestone.Id, weightedAverage, totalWeight);

        return new VerificationPipelineResult(weightedResults, weightedAverage);
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
