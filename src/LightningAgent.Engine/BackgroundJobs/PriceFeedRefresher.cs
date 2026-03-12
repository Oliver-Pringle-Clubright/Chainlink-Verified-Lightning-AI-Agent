using LightningAgent.Core.Configuration;
using LightningAgent.Core.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightningAgent.Engine.BackgroundJobs;

public class PriceFeedRefresher : BackgroundService
{
    private static readonly TimeSpan ChainlinkInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PriceFeedRefresher> _logger;
    private readonly IServiceHealthTracker _healthTracker;
    private readonly CoinGeckoSettings _coinGeckoSettings;

    public PriceFeedRefresher(
        IServiceScopeFactory scopeFactory,
        ILogger<PriceFeedRefresher> logger,
        IServiceHealthTracker healthTracker,
        IOptions<CoinGeckoSettings> coinGeckoSettings)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _healthTracker = healthTracker;
        _coinGeckoSettings = coinGeckoSettings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PriceFeedRefresher started (CoinGecko: {Enabled})", _coinGeckoSettings.Enabled);

        var coinGeckoInterval = TimeSpan.FromSeconds(_coinGeckoSettings.RefreshIntervalSeconds);
        var nextChainlink = DateTime.UtcNow;
        var nextCoinGecko = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;

                // Refresh Chainlink feeds
                if (now >= nextChainlink)
                {
                    await RefreshChainlinkAsync(stoppingToken);
                    nextChainlink = now + ChainlinkInterval;
                }

                // Refresh CoinGecko feeds
                if (_coinGeckoSettings.Enabled && now >= nextCoinGecko)
                {
                    await RefreshCoinGeckoAsync(stoppingToken);
                    nextCoinGecko = now + coinGeckoInterval;
                }

                _healthTracker.RecordSuccess("PriceFeedRefresher");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _healthTracker.RecordFailure("PriceFeedRefresher", ex.Message);
                _logger.LogWarning(ex, "PriceFeedRefresher encountered an error during price refresh");
            }

            try
            {
                // Sleep for shortest interval
                var sleepUntil = _coinGeckoSettings.Enabled
                    ? (nextChainlink < nextCoinGecko ? nextChainlink : nextCoinGecko)
                    : nextChainlink;

                var delay = sleepUntil - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("PriceFeedRefresher stopped");
    }

    private async Task RefreshChainlinkAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var pricingService = scope.ServiceProvider.GetRequiredService<IPricingService>();
        var settings = scope.ServiceProvider.GetRequiredService<IOptions<ChainlinkSettings>>().Value;

        // Always refresh BTC/USD (primary feed)
        if (!string.IsNullOrWhiteSpace(settings.BtcUsdPriceFeedAddress))
        {
            try
            {
                var btcPrice = await pricingService.GetBtcUsdPriceAsync(ct);
                _logger.LogInformation("Refreshed BTC/USD (Chainlink): ${Price:F2}", btcPrice);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh BTC/USD from Chainlink");
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.EthUsdPriceFeedAddress))
        {
            try
            {
                var ethPrice = await pricingService.GetEthUsdPriceAsync(ct);
                _logger.LogInformation("Refreshed ETH/USD (Chainlink): ${Price:F2}", ethPrice);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh ETH/USD from Chainlink");
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.LinkUsdPriceFeedAddress))
        {
            try
            {
                var linkPrice = await pricingService.GetLinkUsdPriceAsync(ct);
                _logger.LogInformation("Refreshed LINK/USD (Chainlink): ${Price:F2}", linkPrice);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh LINK/USD from Chainlink");
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.LinkEthPriceFeedAddress))
        {
            try
            {
                var linkEth = await pricingService.GetLinkEthPriceAsync(ct);
                _logger.LogInformation("Refreshed LINK/ETH (Chainlink): {Price:F8}", linkEth);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh LINK/ETH from Chainlink");
            }
        }
    }

    private async Task RefreshCoinGeckoAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var pricingService = scope.ServiceProvider.GetRequiredService<IPricingService>();

        try
        {
            var prices = await pricingService.RefreshCoinGeckoPricesAsync(ct);
            _logger.LogInformation("Refreshed {Count} CoinGecko prices", prices.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh CoinGecko prices");
        }
    }
}
