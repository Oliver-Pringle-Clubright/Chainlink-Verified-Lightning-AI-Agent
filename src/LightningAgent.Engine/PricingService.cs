using LightningAgent.Core.Configuration;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightningAgent.Engine;

public class PricingService : IPricingService
{
    private readonly IChainlinkPriceFeedClient _priceFeed;
    private readonly IPriceCacheRepository _priceCache;
    private readonly ChainlinkSettings _chainlinkSettings;
    private readonly PricingSettings _pricingSettings;
    private readonly ILogger<PricingService> _logger;

    /// <summary>
    /// Maximum age (in minutes) for a cached price before it is considered stale.
    /// </summary>
    private const int CacheTtlMinutes = 5;

    public PricingService(
        IChainlinkPriceFeedClient priceFeed,
        IPriceCacheRepository priceCache,
        IOptions<ChainlinkSettings> chainlinkSettings,
        IOptions<PricingSettings> pricingSettings,
        ILogger<PricingService> logger)
    {
        _priceFeed = priceFeed;
        _priceCache = priceCache;
        _chainlinkSettings = chainlinkSettings.Value;
        _pricingSettings = pricingSettings.Value;
        _logger = logger;
    }

    public async Task<double> GetBtcUsdPriceAsync(CancellationToken ct = default)
    {
        const string pair = "BTC/USD";

        // 1. Try to get a cached price from the last 5 minutes
        var cached = await _priceCache.GetLatestAsync(pair, ct);
        if (cached is not null && cached.FetchedAt > DateTime.UtcNow.AddMinutes(-CacheTtlMinutes))
        {
            _logger.LogDebug(
                "Using cached BTC/USD price: ${Price:F2} (fetched {Ago:F0}s ago)",
                cached.PriceUsd,
                (DateTime.UtcNow - cached.FetchedAt).TotalSeconds);
            return cached.PriceUsd;
        }

        // 2. Fetch fresh price from Chainlink price feed
        _logger.LogInformation("Fetching fresh BTC/USD price from Chainlink oracle");

        var feedData = await _priceFeed.GetLatestPriceAsync(
            _chainlinkSettings.BtcUsdPriceFeedAddress, ct);

        // Chainlink BTC/USD feeds typically have 8 decimal places
        double price = (double)(feedData.Answer / 100_000_000m);

        _logger.LogInformation(
            "Chainlink BTC/USD price: ${Price:F2} (roundId={RoundId})",
            price, feedData.RoundId);

        // 3. Cache the new price
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
