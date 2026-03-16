using LightningAgentMarketPlace.Core.Configuration;
using LightningAgentMarketPlace.Core.Enums;
using LightningAgentMarketPlace.Core.Interfaces.Data;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using LightningAgentMarketPlace.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightningAgentMarketPlace.Engine;

public class PricingService : IPricingService
{
    private readonly IChainlinkPriceFeedClient _priceFeed;
    private readonly ICoinGeckoClient _coinGecko;
    private readonly IPriceCacheRepository _priceCache;
    private readonly ChainlinkSettings _chainlinkSettings;
    private readonly CoinGeckoSettings _coinGeckoSettings;
    private readonly PricingSettings _pricingSettings;
    private readonly ILogger<PricingService> _logger;

    /// <summary>
    /// Maximum age (in minutes) for a cached price before it is considered stale.
    /// </summary>
    private const int CacheTtlMinutes = 5;

    public PricingService(
        IChainlinkPriceFeedClient priceFeed,
        ICoinGeckoClient coinGecko,
        IPriceCacheRepository priceCache,
        IOptions<ChainlinkSettings> chainlinkSettings,
        IOptions<CoinGeckoSettings> coinGeckoSettings,
        IOptions<PricingSettings> pricingSettings,
        ILogger<PricingService> logger)
    {
        _priceFeed = priceFeed;
        _coinGecko = coinGecko;
        _priceCache = priceCache;
        _chainlinkSettings = chainlinkSettings.Value;
        _coinGeckoSettings = coinGeckoSettings.Value;
        _pricingSettings = pricingSettings.Value;
        _logger = logger;
    }

    public Task<double> GetBtcUsdPriceAsync(CancellationToken ct = default) =>
        FetchPriceAsync("BTC/USD", _chainlinkSettings.BtcUsdPriceFeedAddress, ct);

    public Task<double> GetEthUsdPriceAsync(CancellationToken ct = default) =>
        FetchPriceAsync("ETH/USD", _chainlinkSettings.EthUsdPriceFeedAddress, ct);

    public Task<double> GetLinkUsdPriceAsync(CancellationToken ct = default) =>
        FetchPriceAsync("LINK/USD", _chainlinkSettings.LinkUsdPriceFeedAddress, ct);

    public Task<double> GetLinkEthPriceAsync(CancellationToken ct = default) =>
        FetchPriceAsync("LINK/ETH", _chainlinkSettings.LinkEthPriceFeedAddress, ct);

    public Task<double> GetPriceAsync(string pair, CancellationToken ct = default)
    {
        var normalized = pair.ToUpperInvariant();

        // Check if it's a Chainlink-supported pair first
        var feedAddress = normalized switch
        {
            "BTC/USD" => _chainlinkSettings.BtcUsdPriceFeedAddress,
            "ETH/USD" => _chainlinkSettings.EthUsdPriceFeedAddress,
            "LINK/USD" => _chainlinkSettings.LinkUsdPriceFeedAddress,
            "LINK/ETH" => _chainlinkSettings.LinkEthPriceFeedAddress,
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(feedAddress))
            return FetchPriceAsync(normalized, feedAddress, ct);

        // Fall back to CoinGecko for extended pairs
        if (_coinGeckoSettings.Enabled)
            return FetchCoinGeckoPriceAsync(normalized, ct);

        throw new ArgumentException(
            $"Unknown price pair: {pair}. Chainlink supports: BTC/USD, ETH/USD, LINK/USD, LINK/ETH. " +
            "Enable CoinGecko for additional pairs.");
    }

    /// <summary>
    /// Fetches a price from the Chainlink oracle or cache for any configured pair.
    /// </summary>
    private async Task<double> FetchPriceAsync(string pair, string feedAddress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(feedAddress))
        {
            // If Chainlink address is not configured, try CoinGecko fallback
            if (_coinGeckoSettings.Enabled)
                return await FetchCoinGeckoPriceAsync(pair, ct);

            throw new InvalidOperationException(
                $"Price feed address for {pair} is not configured in Chainlink settings.");
        }

        // 1. Try cache
        var cached = await _priceCache.GetLatestAsync(pair, ct);
        if (cached is not null && cached.FetchedAt > DateTime.UtcNow.AddMinutes(-CacheTtlMinutes))
        {
            _logger.LogDebug(
                "Using cached {Pair} price: ${Price:F2} (fetched {Ago:F0}s ago)",
                pair, cached.PriceUsd, (DateTime.UtcNow - cached.FetchedAt).TotalSeconds);
            return cached.PriceUsd;
        }

        // 2. Fetch from Chainlink
        _logger.LogInformation("Fetching fresh {Pair} price from Chainlink oracle", pair);

        try
        {
            var feedData = await _priceFeed.GetLatestPriceAsync(feedAddress, ct);
            double price = (double)feedData.Answer;

            _logger.LogInformation(
                "Chainlink {Pair} price: ${Price:F8} (roundId={RoundId})",
                pair, price, feedData.RoundId);

            // 3. Cache
            var quote = new PriceQuote
            {
                Pair = pair,
                PriceUsd = price,
                Source = "Chainlink",
                FetchedAt = DateTime.UtcNow
            };

            await _priceCache.CreateAsync(quote, ct);
            return price;
        }
        catch (Exception ex) when (_coinGeckoSettings.Enabled)
        {
            // Chainlink failed — fall back to CoinGecko
            _logger.LogWarning(ex, "Chainlink fetch failed for {Pair}, falling back to CoinGecko", pair);
            return await FetchCoinGeckoPriceAsync(pair, ct);
        }
    }

    /// <summary>
    /// Fetches a single price from CoinGecko (or cache).
    /// </summary>
    private async Task<double> FetchCoinGeckoPriceAsync(string pair, CancellationToken ct)
    {
        // 1. Try cache first
        var cached = await _priceCache.GetLatestAsync(pair, ct);
        if (cached is not null && cached.FetchedAt > DateTime.UtcNow.AddMinutes(-CacheTtlMinutes))
        {
            _logger.LogDebug("Using cached CoinGecko {Pair} price: ${Price:F4}", pair, cached.PriceUsd);
            return cached.PriceUsd;
        }

        // 2. Resolve CoinGecko coin ID from pair
        var coinId = Services.CoinGeckoClient.ResolveCoinId(pair);
        if (coinId is null)
            throw new ArgumentException($"Unknown price pair: {pair}. Not mapped to a CoinGecko coin ID.");

        // 3. Fetch from CoinGecko
        var price = await _coinGecko.GetPriceAsync(coinId, ct);
        if (price is null)
            throw new InvalidOperationException($"CoinGecko returned no data for {pair} (coinId={coinId}).");

        // 4. Cache
        var quote = new PriceQuote
        {
            Pair = pair,
            PriceUsd = price.PriceUsd,
            Source = "CoinGecko",
            FetchedAt = DateTime.UtcNow
        };
        await _priceCache.CreateAsync(quote, ct);

        return price.PriceUsd;
    }

    /// <summary>
    /// Bulk-refreshes all CoinGecko prices and caches them.
    /// </summary>
    public async Task<Dictionary<string, double>> RefreshCoinGeckoPricesAsync(CancellationToken ct = default)
    {
        if (!_coinGeckoSettings.Enabled)
        {
            _logger.LogDebug("CoinGecko is disabled, skipping refresh");
            return new Dictionary<string, double>();
        }

        var prices = await _coinGecko.GetAllPricesAsync(ct);
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var (pair, cgPrice) in prices)
        {
            var quote = new PriceQuote
            {
                Pair = pair,
                PriceUsd = cgPrice.PriceUsd,
                Source = "CoinGecko",
                FetchedAt = DateTime.UtcNow
            };

            await _priceCache.CreateAsync(quote, ct);
            result[pair] = cgPrice.PriceUsd;
        }

        _logger.LogInformation("Cached {Count} CoinGecko prices", result.Count);
        return result;
    }

    /// <summary>
    /// Returns all cached prices from both sources.
    /// </summary>
    public async Task<IReadOnlyList<PriceQuote>> GetAllCachedPricesAsync(CancellationToken ct = default)
    {
        return await _priceCache.GetAllLatestAsync(ct);
    }

    public async Task<long> CalculatePriceSatsAsync(double usdAmount, CancellationToken ct = default)
    {
        var btcPrice = await GetBtcUsdPriceAsync(ct);

        if (btcPrice <= 0)
        {
            _logger.LogWarning(
                "BTC price is {Price}, cannot calculate sats for ${Usd}",
                btcPrice, usdAmount);
            return 0;
        }

        // Convert: sats = (usdAmount / btcPrice) * 100,000,000
        double sats = (usdAmount / btcPrice) * 100_000_000.0;
        long result = (long)Math.Round(sats);

        _logger.LogDebug(
            "Converted ${Usd:F2} to {Sats} sats (BTC price=${BtcPrice:F2})",
            usdAmount, result, btcPrice);

        return result;
    }

    public async Task<(long sats, double usd)> EstimateTaskCostAsync(
        TaskItem task,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Estimating cost for task {TaskId} (type={TaskType})",
            task.Id, task.TaskType);

        // Base cost estimation by TaskType (in USD)
        double usdEstimate = EstimateBaseUsdCost(task);

        // Apply margin from settings
        usdEstimate *= _pricingSettings.MarginMultiplier;

        // Convert to sats
        long sats = await CalculatePriceSatsAsync(usdEstimate, ct);

        // Clamp to configured bounds
        sats = Math.Clamp(sats, _pricingSettings.MinPriceSats, _pricingSettings.MaxPriceSats);

        _logger.LogInformation(
            "Task {TaskId} estimated cost: {Sats} sats (${Usd:F2})",
            task.Id, sats, usdEstimate);

        return (sats, usdEstimate);
    }

    /// <summary>
    /// Estimates the base USD cost of a task based on its type and description length.
    /// </summary>
    private static double EstimateBaseUsdCost(TaskItem task)
    {
        int descriptionLength = task.Description?.Length ?? 0;

        return task.TaskType switch
        {
            // Code: $5-50 depending on description length
            TaskType.Code => ScaleByLength(descriptionLength, minUsd: 5.0, maxUsd: 50.0),

            // Data: $2-20
            TaskType.Data => ScaleByLength(descriptionLength, minUsd: 2.0, maxUsd: 20.0),

            // Text: $1-10
            TaskType.Text => ScaleByLength(descriptionLength, minUsd: 1.0, maxUsd: 10.0),

            // Image: $3-15
            TaskType.Image => ScaleByLength(descriptionLength, minUsd: 3.0, maxUsd: 15.0),

            _ => 5.0 // Default
        };
    }

    /// <summary>
    /// Scales the USD cost linearly between minUsd and maxUsd based on description
    /// length (0 chars = min, 1000+ chars = max).
    /// </summary>
    private static double ScaleByLength(int descriptionLength, double minUsd, double maxUsd)
    {
        const int maxLength = 1000;
        double ratio = Math.Clamp((double)descriptionLength / maxLength, 0.0, 1.0);
        return minUsd + (ratio * (maxUsd - minUsd));
    }
}
