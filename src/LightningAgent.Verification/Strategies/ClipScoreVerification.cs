using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace LightningAgent.Verification.Strategies;

public class ClipScoreVerification : IVerificationStrategy
{
    private readonly ILogger<ClipScoreVerification> _logger;

    public ClipScoreVerification(ILogger<ClipScoreVerification> logger)
    {
        _logger = logger;
    }

    public VerificationStrategyType StrategyType => VerificationStrategyType.ClipScore;

    public bool CanHandle(TaskType taskType) => taskType == TaskType.Image;

    public Task<VerificationResult> VerifyAsync(
        Milestone milestone,
        byte[] agentOutput,
        CancellationToken ct = default)
    {
        _logger.LogWarning(
            "CLIP score verification for milestone {MilestoneId}: CLIP integration is not configured. " +
            "Image verification requires an external CLIP API integration.",
            milestone.Id);

        // CLIP scoring service is not configured. Return a failing result so that
        // image milestones do not silently pass with a fake score. In production,
        // this would call a CLIP scoring API to compare the generated image
        // against the task description.
        const double placeholderScore = 0.7; // Below default pass threshold of 0.8
        const bool passed = false;
        const string details = "CLIP scoring service not configured. " +
            "Image verification requires external CLIP API integration.";

        _logger.LogInformation(
            "CLIP score verification result: Score={Score}, Passed={Passed}",
            placeholderScore,
            passed);

        return Task.FromResult(new VerificationResult(
            Score: placeholderScore,
            Passed: passed,
            Details: details,
            StrategyType: VerificationStrategyType.ClipScore));
    }
}
