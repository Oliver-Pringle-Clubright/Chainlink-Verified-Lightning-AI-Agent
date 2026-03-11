using LightningAgent.Core.Models;

namespace LightningAgent.Core.Interfaces.Services;

public interface IVerificationPipeline
{
    Task<VerificationPipelineResult> RunVerificationAsync(Milestone milestone, byte[] agentOutput, CancellationToken ct = default);
}
