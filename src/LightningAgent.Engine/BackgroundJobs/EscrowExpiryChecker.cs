using LightningAgent.Core.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LightningAgent.Engine.BackgroundJobs;

public class EscrowExpiryChecker : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EscrowExpiryChecker> _logger;

    public EscrowExpiryChecker(
        IServiceScopeFactory scopeFactory,
        ILogger<EscrowExpiryChecker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EscrowExpiryChecker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var escrowManager = scope.ServiceProvider.GetRequiredService<IEscrowManager>();

                var cancelledCount = await escrowManager.CheckExpiredEscrowsAsync(stoppingToken);

                if (cancelledCount > 0)
                {
                    _logger.LogInformation(
                        "EscrowExpiryChecker cancelled {Count} expired escrows", cancelledCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown — do not log as an error
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EscrowExpiryChecker encountered an error during check cycle");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("EscrowExpiryChecker stopped");
    }
}
