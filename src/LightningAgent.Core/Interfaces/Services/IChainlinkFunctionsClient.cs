using LightningAgent.Core.Models.Chainlink;

namespace LightningAgent.Core.Interfaces.Services;

public interface IChainlinkFunctionsClient
{
    Task<string> SendRequestAsync(ChainlinkFunctionRequest request, CancellationToken ct = default);
    Task<ChainlinkFunctionResponse?> GetResponseAsync(string requestId, CancellationToken ct = default);
}
