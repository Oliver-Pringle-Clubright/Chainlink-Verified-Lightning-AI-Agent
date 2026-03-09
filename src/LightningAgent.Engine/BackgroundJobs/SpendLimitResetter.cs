using LightningAgent.Core.Interfaces.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LightningAgent.Engine.BackgroundJobs;

public class SpendLimitResetter : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    private readonly ISpendLimitService _spendLimitService;
    private readonly ILogger<SpendLimitResetter> _logger;

    public SpendLimitResetter(
        ISpendLimitService spendLimitService,
        ILogger<SpendLimitResetter> logger)
    {
        _spendLimitService = spendLimitService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SpendLimitResetter started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var resetCount = await _spendLimitService.ResetExpiredPeriodsAsync(stoppingToken);

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
