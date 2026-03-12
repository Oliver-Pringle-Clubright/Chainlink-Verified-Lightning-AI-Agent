using LightningAgent.Core.Models;

namespace LightningAgent.Core.Interfaces.Data;

public interface IPriceCacheRepository
{
    Task<PriceQuote?> GetLatestAsync(string pair, CancellationToken ct = default);
    Task<IReadOnlyList<PriceQuote>> GetAllLatestAsync(CancellationToken ct = default);
    Task<int> CreateAsync(PriceQuote quote, CancellationToken ct = default);
    Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct = default);
}
