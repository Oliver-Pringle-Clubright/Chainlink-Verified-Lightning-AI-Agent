using LightningAgent.Core.Models.Chainlink;

namespace LightningAgent.Core.Interfaces.Data;

public interface ICcipMessageRepository
{
    Task<int> CreateAsync(CcipMessage message, CancellationToken ct = default);
    Task<CcipMessage?> GetByMessageIdAsync(string messageId, CancellationToken ct = default);
    Task<List<CcipMessage>> GetPendingOutboundAsync(CancellationToken ct = default);
    Task<List<CcipMessage>> GetByTaskIdAsync(int taskId, CancellationToken ct = default);
    Task<List<CcipMessage>> GetByAgentIdAsync(int agentId, CancellationToken ct = default);
    Task<List<CcipMessage>> GetRecentAsync(int limit = 50, CancellationToken ct = default);
    Task UpdateAsync(CcipMessage message, CancellationToken ct = default);
}
