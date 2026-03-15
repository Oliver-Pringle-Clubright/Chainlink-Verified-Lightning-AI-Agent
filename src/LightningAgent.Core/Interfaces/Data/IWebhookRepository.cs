using LightningAgent.Core.Models;

namespace LightningAgent.Core.Interfaces.Data;

public interface IWebhookRepository
{
    Task<int> CreateAsync(WebhookSubscription subscription, CancellationToken ct = default);
    Task<WebhookSubscription?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<WebhookSubscription>> GetByAgentIdAsync(int agentId, CancellationToken ct = default);
    Task<IReadOnlyList<WebhookSubscription>> GetActiveByEventAsync(string eventType, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
