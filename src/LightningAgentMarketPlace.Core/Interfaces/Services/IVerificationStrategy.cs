using LightningAgentMarketPlace.Core.Enums;
using LightningAgentMarketPlace.Core.Models;

namespace LightningAgentMarketPlace.Core.Interfaces.Services;

public interface IVerificationStrategy
{
    VerificationStrategyType StrategyType { get; }
    bool CanHandle(TaskType taskType);
    Task<VerificationResult> VerifyAsync(Milestone milestone, byte[] agentOutput, CancellationToken ct = default);
}
