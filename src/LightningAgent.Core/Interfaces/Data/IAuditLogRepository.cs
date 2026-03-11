using LightningAgent.Core.Models;

namespace LightningAgent.Core.Interfaces.Data;

public interface IAuditLogRepository
{
    Task<int> CreateAsync(AuditLogEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLogEntry>> GetByAgentAsync(int agentId, int limit = 100, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLogEntry>> GetByEntityAsync(string entityType, int entityId, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLogEntry>> GetRecentAsync(int count, CancellationToken ct = default);
}
