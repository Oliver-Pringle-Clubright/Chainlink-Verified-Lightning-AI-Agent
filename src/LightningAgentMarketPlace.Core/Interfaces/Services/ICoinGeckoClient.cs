namespace LightningAgentMarketPlace.Core.Interfaces.Services;

public interface ICoinGeckoClient
{
    /// <summary>
    /// Fetches current USD prices for all configured coins from CoinGecko.
    /// Returns a dictionary of pair (e.g. "BTC/USD") to price.
    /// </summary>
    Task<Dictionary<string, CoinGeckoPrice>> GetAllPricesAsync(CancellationToken ct = default);

    /// <summary>
    /// Fetches the USD price for a single coin by its CoinGecko ID.
    /// </summary>
    Task<CoinGeckoPrice?> GetPriceAsync(string coinId, CancellationToken ct = default);

    /// <summary>
    /// Returns the list of supported pair names (e.g. "BTC/USD", "SOL/USD").
    /// </summary>
    IReadOnlyList<string> GetSupportedPairs();
}

public record CoinGeckoPrice(
    string Pair,
    string CoinId,
    double PriceUsd,
    double? Change24h,
    long? LastUpdatedAt);
