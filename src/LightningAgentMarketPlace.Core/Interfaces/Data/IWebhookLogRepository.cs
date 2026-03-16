using LightningAgentMarketPlace.Core.Models;

namespace LightningAgentMarketPlace.Core.Interfaces.Data;

public interface IWebhookLogRepository
{
    Task<int> LogAsync(WebhookDeliveryLog entry, CancellationToken ct = default);
    Task UpdateAsync(WebhookDeliveryLog entry, CancellationToken ct = default);
    Task<IReadOnlyList<WebhookDeliveryLog>> GetFailedAsync(int limit = 100, CancellationToken ct = default);
    Task<IReadOnlyList<WebhookDeliveryLog>> GetRecentAsync(int count, CancellationToken ct = default);
    Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct = default);
}
