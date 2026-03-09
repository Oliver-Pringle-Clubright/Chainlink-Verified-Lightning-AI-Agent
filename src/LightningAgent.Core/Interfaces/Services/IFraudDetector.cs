using LightningAgent.Core.Models.AI;

namespace LightningAgent.Core.Interfaces.Services;

public interface IFraudDetector
{
    Task<FraudReport?> DetectSybilAsync(int agentId, CancellationToken ct = default);
    Task<FraudReport?> DetectRecycledOutputAsync(int milestoneId, byte[] output, CancellationToken ct = default);
    Task<double> GetAnomalyScoreAsync(int agentId, CancellationToken ct = default);
}
