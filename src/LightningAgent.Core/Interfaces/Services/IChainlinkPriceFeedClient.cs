using LightningAgent.Core.Models.Chainlink;

namespace LightningAgent.Core.Interfaces.Services;

public interface IChainlinkPriceFeedClient
{
    Task<PriceFeedData> GetLatestPriceAsync(string priceFeedAddress, CancellationToken ct = default);
}
