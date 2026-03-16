using LightningAgentMarketPlace.Core.Models.Chainlink;

namespace LightningAgentMarketPlace.Core.Interfaces.Services;

public interface IChainlinkVrfClient
{
    /// <summary>
    /// Sends a VRF randomness request to the coordinator. Returns immediately
    /// with the request ID; randomness is fulfilled asynchronously by the coordinator.
    /// </summary>
    Task<ChainlinkVrfRequest> RequestRandomnessAsync(int numWords, CancellationToken ct = default);

    /// <summary>
    /// Checks if a VRF request has been fulfilled and returns the random words.
    /// Returns null if not yet fulfilled.
    /// </summary>
    Task<ChainlinkVrfRequest?> GetFulfillmentAsync(string requestId, CancellationToken ct = default);

    /// <summary>
    /// Returns true if the VRF coordinator and consumer are configured.
    /// </summary>
    bool IsConfigured { get; }
}
