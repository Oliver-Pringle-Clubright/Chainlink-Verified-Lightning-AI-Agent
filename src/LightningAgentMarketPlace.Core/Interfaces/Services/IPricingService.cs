using LightningAgentMarketPlace.Core.Models;

namespace LightningAgentMarketPlace.Core.Interfaces.Services;

public interface IPricingService
{
    Task<double> GetBtcUsdPriceAsync(CancellationToken ct = default);
    Task<double> GetEthUsdPriceAsync(CancellationToken ct = default);
    Task<double> GetLinkUsdPriceAsync(CancellationToken ct = default);
    Task<double> GetLinkEthPriceAsync(CancellationToken ct = default);
    Task<double> GetPriceAsync(string pair, CancellationToken ct = default);
    Task<long> CalculatePriceSatsAsync(double usdAmount, CancellationToken ct = default);
    Task<(long sats, double usd)> EstimateTaskCostAsync(TaskItem task, CancellationToken ct = default);

    /// <summary>
    /// Fetches all CoinGecko prices and caches them. Returns pair → price map.
    /// </summary>
    Task<Dictionary<string, double>> RefreshCoinGeckoPricesAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns all cached prices from both Chainlink and CoinGecko sources.
    /// </summary>
    Task<IReadOnlyList<PriceQuote>> GetAllCachedPricesAsync(CancellationToken ct = default);
}
