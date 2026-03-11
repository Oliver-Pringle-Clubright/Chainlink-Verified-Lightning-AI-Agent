using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace LightningAgent.Verification;

/// <summary>
/// Runs verification plugins that match the task type of a given milestone and
/// aggregates their results.
/// </summary>
public class PluginVerificationRunner
{
    private readonly IEnumerable<IVerificationPlugin> _plugins;
    private readonly ILogger<PluginVerificationRunner> _logger;

    public PluginVerificationRunner(
        IEnumerable<IVerificationPlugin> plugins,
        ILogger<PluginVerificationRunner> logger)
    {
        _plugins = plugins;
        _logger = logger;
    }

    /// <summary>
    /// Selects verification plugins whose <see cref="IVerificationPlugin.SupportedTaskTypes"/>
    /// contain the specified <paramref name="taskType"/>, runs them against the milestone
    /// output, and returns the aggregated results.
    /// </summary>
    public async Task<List<VerificationResult>> RunPluginsAsync(
        Milestone milestone,
        string output,
        string taskType,
        CancellationToken ct = default)
    {
        var matchingPlugins = _plugins
            .Where(p => p.SupportedTaskTypes
                .Any(t => t.Equals(taskType, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        _logger.LogInformation(
            "PluginVerificationRunner: Found {Count} matching plugins for task type '{TaskType}': {Names}",
            matchingPlugins.Count,
            taskType,
            string.Join(", ", matchingPlugins.Select(p => p.Name)));

        if (matchingPlugins.Count == 0)
        {
            _logger.LogDebug(
                "No verification plugins matched task type '{TaskType}' for milestone {MilestoneId}",
                taskType, milestone.Id);
            return [];
        }

        var tasks = matchingPlugins
            .Select(plugin => RunPluginSafeAsync(plugin, milestone, output, ct))
            .ToList();

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    private async Task<VerificationResult> RunPluginSafeAsync(
        IVerificationPlugin plugin,
        Milestone milestone,
        string output,
        CancellationToken ct)
    {
        try
        {
            _logger.LogDebug(
                "Running verification plugin '{Plugin}' for milestone {MilestoneId}",
                plugin.Name, milestone.Id);

            return await plugin.VerifyAsync(milestone, output, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Verification plugin '{Plugin}' threw an exception for milestone {MilestoneId}",
                plugin.Name, milestone.Id);

            return new VerificationResult(
                Score: 0.0,
                Passed: false,
                Details: $"Plugin '{plugin.Name}' failed: {ex.Message}",
                StrategyType: Core.Enums.VerificationStrategyType.AiJudge);
        }
    }
}
