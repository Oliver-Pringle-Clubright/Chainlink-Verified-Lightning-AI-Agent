using LightningAgentMarketPlace.Core.Models.Chainlink;

namespace LightningAgentMarketPlace.Core.Interfaces.Services;

public interface IChainlinkPriceFeedClient
{
    Task<PriceFeedData> GetLatestPriceAsync(string priceFeedAddress, CancellationToken ct = default);
    Task<PriceFeedData> GetLatestPriceAsync(string priceFeedAddress, string rpcUrl, CancellationToken ct = default);
}
