using LightningAgent.Core.Models.Chainlink;

namespace LightningAgent.Core.Interfaces.Services;

public interface IChainlinkPriceFeedClient
{
    Task<PriceFeedData> GetLatestPriceAsync(string priceFeedAddress, CancellationToken ct = default);
    Task<PriceFeedData> GetLatestPriceAsync(string priceFeedAddress, string rpcUrl, CancellationToken ct = default);
}
