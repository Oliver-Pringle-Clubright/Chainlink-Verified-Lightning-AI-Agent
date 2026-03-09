using LightningAgent.Core.Models;

namespace LightningAgent.Core.Interfaces.Services;

public interface IPricingService
{
    Task<double> GetBtcUsdPriceAsync(CancellationToken ct = default);
    Task<long> CalculatePriceSatsAsync(double usdAmount, CancellationToken ct = default);
    Task<(long sats, double usd)> EstimateTaskCostAsync(TaskItem task, CancellationToken ct = default);
}
