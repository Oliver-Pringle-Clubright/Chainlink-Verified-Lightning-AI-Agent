using LightningAgentMarketPlace.Core.Enums;

namespace LightningAgentMarketPlace.Core.Models;

public record VerificationResult(
    double Score,
    bool Passed,
    string? Details,
    VerificationStrategyType StrategyType);
