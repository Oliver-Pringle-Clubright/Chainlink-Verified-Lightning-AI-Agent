using LightningAgent.Core.Models;

namespace LightningAgent.Core.Interfaces.Services;

public interface IPricingService
{
    Task<double> GetBtcUsdPriceAsync(CancellationToken ct = default);
    Task<double> GetEthUsdPriceAsync(CancellationToken ct = default);
    Task<double> GetLinkUsdPriceAsync(CancellationToken ct = default);
    Task<double> GetLinkEthPriceAsync(CancellationToken ct = default);
    Task<double> GetPriceAsync(string pair, CancellationToken ct = default);
    Task<long> CalculatePriceSatsAsync(double usdAmount, CancellationToken ct = default);
    Task<(long sats, double usd)> EstimateTaskCostAsync(TaskItem task, CancellationToken ct = default);
}
