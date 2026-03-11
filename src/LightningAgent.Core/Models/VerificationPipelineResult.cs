namespace LightningAgent.Core.Models;

/// <summary>
/// Encapsulates the full output of the verification pipeline, including
/// individual strategy results and the overall weighted score.
/// </summary>
public record VerificationPipelineResult(
    List<VerificationResult> Results,
    double WeightedScore);
