using LightningAgent.Core.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LightningAgent.Engine.BackgroundJobs;

public class SpendLimitResetter : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SpendLimitResetter> _logger;

    public SpendLimitResetter(
        IServiceScopeFactory scopeFactory,
        ILogger<SpendLimitResetter> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SpendLimitResetter started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var spendLimitService = scope.ServiceProvider.GetRequiredService<ISpendLimitService>();

                var resetCount = await spendLimitService.ResetExpiredPeriodsAsync(stoppingToken);

                if (resetCount > 0)
                {
                    _logger.LogInformation(
                        "SpendLimitResetter reset {Count} expired spend limit periods", resetCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown — do not log as an error
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SpendLimitResetter encountered an error during reset cycle");
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

        _logger.LogInformation("SpendLimitResetter stopped");
    }
}
