using LightningAgent.Core.Models.Chainlink;

namespace LightningAgent.Core.Interfaces.Services;

public interface IChainlinkVrfClient
{
    Task<ChainlinkVrfRequest> RequestRandomnessAsync(int numWords, CancellationToken ct = default);
}
