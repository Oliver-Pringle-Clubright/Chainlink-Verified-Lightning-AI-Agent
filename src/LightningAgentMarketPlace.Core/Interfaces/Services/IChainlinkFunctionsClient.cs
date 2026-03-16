using LightningAgentMarketPlace.Core.Models.Chainlink;

namespace LightningAgentMarketPlace.Core.Interfaces.Services;

public interface IChainlinkFunctionsClient
{
    Task<string> SendRequestAsync(ChainlinkFunctionRequest request, CancellationToken ct = default);
    Task<ChainlinkFunctionResponse?> GetResponseAsync(string requestId, CancellationToken ct = default);
    bool IsRequestPending(string requestId);
}
