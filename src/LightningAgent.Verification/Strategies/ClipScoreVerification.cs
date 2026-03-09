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
        _logger.LogInformation(
            "Running CLIP score verification for milestone {MilestoneId} (stub implementation)",
            milestone.Id);

        // Stub implementation: in production this would call a CLIP scoring API
        // to compare the generated image against the task description.
        // For now, return a default passing score if the output contains image data.

        var hasContent = agentOutput.Length > 0;
        var score = hasContent ? 0.85 : 0.0;
        var passed = hasContent;
        var details = hasContent
            ? "CLIP score verification passed (stub: image data present)"
            : "CLIP score verification failed: no image data provided";

        _logger.LogInformation(
            "CLIP score verification result: Score={Score}, Passed={Passed}",
            score,
            passed);

        return Task.FromResult(new VerificationResult(
            Score: score,
            Passed: passed,
            Details: details,
            StrategyType: VerificationStrategyType.ClipScore));
    }
}
