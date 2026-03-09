using LightningAgent.Core.Enums;
using LightningAgent.Core.Models;

namespace LightningAgent.Core.Interfaces.Services;

public interface IVerificationStrategy
{
    VerificationStrategyType StrategyType { get; }
    bool CanHandle(TaskType taskType);
    Task<VerificationResult> VerifyAsync(Milestone milestone, byte[] agentOutput, CancellationToken ct = default);
}
