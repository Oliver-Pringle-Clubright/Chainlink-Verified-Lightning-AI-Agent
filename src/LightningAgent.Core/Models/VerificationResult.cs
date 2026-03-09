using LightningAgent.Core.Enums;

namespace LightningAgent.Core.Models;

public record VerificationResult(
    double Score,
    bool Passed,
    string? Details,
    VerificationStrategyType StrategyType);
