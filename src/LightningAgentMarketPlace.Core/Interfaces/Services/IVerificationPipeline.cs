using LightningAgentMarketPlace.Core.Models;

namespace LightningAgentMarketPlace.Core.Interfaces.Services;

public interface IVerificationPipeline
{
    Task<VerificationPipelineResult> RunVerificationAsync(Milestone milestone, byte[] agentOutput, CancellationToken ct = default);
}
