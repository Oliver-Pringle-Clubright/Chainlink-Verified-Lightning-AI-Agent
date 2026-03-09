using LightningAgent.Core.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LightningAgent.Engine.BackgroundJobs;

public class PriceFeedRefresher : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PriceFeedRefresher> _logger;

    public PriceFeedRefresher(
        IServiceScopeFactory scopeFactory,
        ILogger<PriceFeedRefresher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
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

                var btcUsdPrice = await pricingService.GetBtcUsdPriceAsync(stoppingToken);

                _logger.LogInformation(
                    "PriceFeedRefresher refreshed BTC/USD price: ${Price:F2}", btcUsdPrice);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "PriceFeedRefresher encountered an error during price refresh (Chainlink price feed may not be configured)");
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
