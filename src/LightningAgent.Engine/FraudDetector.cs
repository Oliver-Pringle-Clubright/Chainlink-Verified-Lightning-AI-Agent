using System.Text;
using LightningAgent.AI.Fraud;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models.AI;
using Microsoft.Extensions.Logging;

namespace LightningAgent.Engine;

/// <summary>
/// Orchestrates fraud detection by delegating to AI-based sybil and recycled-output detectors,
/// and computing anomaly scores from agent reputation data.
/// </summary>
public class FraudDetector : IFraudDetector
{
    private readonly SybilDetector _sybilDetector;
    private readonly RecycledOutputDetector _recycledDetector;
    private readonly IAgentReputationRepository _reputationRepo;
    private readonly ILogger<FraudDetector> _logger;

    public FraudDetector(
        SybilDetector sybilDetector,
        RecycledOutputDetector recycledDetector,
        IAgentReputationRepository reputationRepo,
        ILogger<FraudDetector> logger)
    {
        _sybilDetector = sybilDetector;
        _recycledDetector = recycledDetector;
        _reputationRepo = reputationRepo;
        _logger = logger;
    }

    public async Task<FraudReport?> DetectSybilAsync(int agentId, CancellationToken ct = default)
    {
        _logger.LogInformation("Running sybil detection for agent {AgentId}", agentId);

        var reputation = await _reputationRepo.GetByAgentIdAsync(agentId, ct);

        if (reputation is null)
        {
            _logger.LogWarning("No reputation data found for agent {AgentId}. Skipping sybil detection", agentId);
            return null;
        }

        // Build a behavior data string from reputation stats for AI analysis
        var behaviorData = new StringBuilder();
        behaviorData.AppendLine($"Agent ID: {agentId}");
        behaviorData.AppendLine($"Total Tasks: {reputation.TotalTasks}");
        behaviorData.AppendLine($"Completed Tasks: {reputation.CompletedTasks}");
        behaviorData.AppendLine($"Verification Passes: {reputation.VerificationPasses}");
        behaviorData.AppendLine($"Verification Fails: {reputation.VerificationFails}");
        behaviorData.AppendLine($"Dispute Count: {reputation.DisputeCount}");
        behaviorData.AppendLine($"Average Response Time (sec): {reputation.AvgResponseTimeSec:F1}");
        behaviorData.AppendLine($"Reputation Score: {reputation.ReputationScore:F3}");
        behaviorData.AppendLine($"Last Updated: {reputation.LastUpdated:O}");

        var report = await _sybilDetector.DetectAsync(behaviorData.ToString(), ct);

        if (report is not null)
        {
            _logger.LogWarning(
                "Sybil behavior detected for agent {AgentId}: {FraudType} (confidence={Confidence:F2})",
                agentId, report.FraudType, report.Confidence);
        }

        return report;
    }

    public async Task<FraudReport?> DetectRecycledOutputAsync(
        int milestoneId,
        byte[] output,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Running recycled output detection for milestone {MilestoneId}", milestoneId);

        var currentOutput = Encoding.UTF8.GetString(output);

        // In production, this would query the database for previous outputs
        // from the same agent or similar tasks. For now, pass an empty list.
        var previousOutputs = new List<string>();

        var report = await _recycledDetector.DetectAsync(currentOutput, previousOutputs, ct);

        if (report is not null)
        {
            _logger.LogWarning(
                "Recycled output detected for milestone {MilestoneId}: {FraudType} (confidence={Confidence:F2})",
                milestoneId, report.FraudType, report.Confidence);
        }

        return report;
    }

    public async Task<double> GetAnomalyScoreAsync(int agentId, CancellationToken ct = default)
    {
        _logger.LogInformation("Calculating anomaly score for agent {AgentId}", agentId);

        var reputation = await _reputationRepo.GetByAgentIdAsync(agentId, ct);

        if (reputation is null)
        {
            _logger.LogWarning(
                "No reputation data found for agent {AgentId}. Returning neutral anomaly score",
                agentId);
            return 0.0;
        }

        // Calculate anomaly score based on suspicious patterns:
        // 1. High task count combined with low verification rate
        // 2. High dispute rate relative to completed tasks
        double anomalyScore = 0.0;

        // Factor 1: Verification anomaly
        // High task count but low verification pass rate is suspicious
        if (reputation.TotalTasks > 0)
        {
            int totalVerifications = reputation.VerificationPasses + reputation.VerificationFails;
            double verificationRate = totalVerifications > 0
                ? (double)reputation.VerificationPasses / totalVerifications
                : 1.0; // No verifications yet is not anomalous

            // If verification rate is below 50% with significant tasks, that's anomalous
            if (reputation.TotalTasks >= 5 && verificationRate < 0.5)
            {
                anomalyScore += (0.5 - verificationRate) * 1.0; // up to 0.5 contribution
            }
        }

        // Factor 2: Dispute rate anomaly
        if (reputation.CompletedTasks > 0)
        {
            double disputeRate = (double)reputation.DisputeCount / reputation.CompletedTasks;

            // More than 20% dispute rate is anomalous
            if (disputeRate > 0.2)
            {
                anomalyScore += Math.Min(disputeRate, 1.0) * 0.5; // up to 0.5 contribution
            }
        }
        else if (reputation.DisputeCount > 0)
        {
            // Has disputes but no completed tasks -- very anomalous
            anomalyScore += 0.5;
        }

        anomalyScore = Math.Clamp(anomalyScore, 0.0, 1.0);

        _logger.LogInformation(
            "Anomaly score for agent {AgentId}: {Score:F3} (tasks={Tasks}, disputes={Disputes})",
            agentId, anomalyScore, reputation.TotalTasks, reputation.DisputeCount);

        return anomalyScore;
    }
}
