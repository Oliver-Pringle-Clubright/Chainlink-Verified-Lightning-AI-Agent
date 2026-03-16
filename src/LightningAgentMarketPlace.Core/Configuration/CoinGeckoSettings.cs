namespace LightningAgentMarketPlace.Core.Configuration;

public class CoinGeckoSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// CoinGecko API base URL. Free: https://api.coingecko.com/api/v3
    /// Pro: https://pro-api.coingecko.com/api/v3
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.coingecko.com/api/v3";

    /// <summary>
    /// Optional CoinGecko Pro API key for higher rate limits.
    /// Leave empty for free tier (~30 requests/min).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated CoinGecko coin IDs to fetch prices for.
    /// </summary>
    public string CoinIds { get; set; } =
        "bitcoin,ethereum,chainlink,solana,avalanche-2,cardano,polkadot,uniswap," +
        "matic-network,arbitrum,optimism,cosmos,near,dogecoin,ripple,binancecoin," +
        "usd-coin,tether";

    /// <summary>
    /// Refresh interval in seconds (default 60s — CoinGecko free tier updates every ~60s).
    /// </summary>
    public int RefreshIntervalSeconds { get; set; } = 60;
}
