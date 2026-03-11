using LightningAgent.Core.Configuration;
using LightningAgent.Core.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightningAgent.Engine.BackgroundJobs;

public class PriceFeedRefresher : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PriceFeedRefresher> _logger;
    private readonly IServiceHealthTracker _healthTracker;

    public PriceFeedRefresher(
        IServiceScopeFactory scopeFactory,
        ILogger<PriceFeedRefresher> logger,
        IServiceHealthTracker healthTracker)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _healthTracker = healthTracker;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PriceFeedRefresher started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var pricingService = scope.ServiceProvider.GetRequiredService<IPricingService>();
                var settings = scope.ServiceProvider.GetRequiredService<IOptions<ChainlinkSettings>>().Value;

                // Always refresh BTC/USD (primary feed)
                if (!string.IsNullOrWhiteSpace(settings.BtcUsdPriceFeedAddress))
                {
                    try
                    {
                        var btcPrice = await pricingService.GetBtcUsdPriceAsync(stoppingToken);
                        _logger.LogInformation("PriceFeedRefresher refreshed BTC/USD price: ${Price:F2}", btcPrice);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "PriceFeedRefresher failed to refresh BTC/USD");
                    }
                }

                // Refresh ETH/USD if configured
                if (!string.IsNullOrWhiteSpace(settings.EthUsdPriceFeedAddress))
                {
                    try
                    {
                        var ethPrice = await pricingService.GetEthUsdPriceAsync(stoppingToken);
                        _logger.LogInformation("PriceFeedRefresher refreshed ETH/USD price: ${Price:F2}", ethPrice);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "PriceFeedRefresher failed to refresh ETH/USD");
                    }
                }

                // Refresh LINK/USD if configured
                if (!string.IsNullOrWhiteSpace(settings.LinkUsdPriceFeedAddress))
                {
                    try
                    {
                        var linkPrice = await pricingService.GetLinkUsdPriceAsync(stoppingToken);
                        _logger.LogInformation("PriceFeedRefresher refreshed LINK/USD price: ${Price:F2}", linkPrice);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "PriceFeedRefresher failed to refresh LINK/USD");
                    }
                }

                // Refresh LINK/ETH if configured
                if (!string.IsNullOrWhiteSpace(settings.LinkEthPriceFeedAddress))
                {
                    try
                    {
                        var linkEth = await pricingService.GetLinkEthPriceAsync(stoppingToken);
                        _logger.LogInformation("PriceFeedRefresher refreshed LINK/ETH price: {Price:F8}", linkEth);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "PriceFeedRefresher failed to refresh LINK/ETH");
                    }
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
                _logger.LogWarning(
                    ex,
                    "PriceFeedRefresher encountered an error during price refresh");
            }

            try
            {
                await Task.Delay(RefreshInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("PriceFeedRefresher stopped");
    }
}
