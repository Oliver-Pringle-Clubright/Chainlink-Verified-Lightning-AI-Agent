using LightningAgent.Core.Models;

namespace LightningAgent.Core.Interfaces.Data;

public interface IAuditLogRepository
{
    Task<IReadOnlyList<AuditLogEntry>> GetByEntityAsync(string entityType, int entityId, CancellationToken ct = default);
    Task<int> CreateAsync(AuditLogEntry entry, CancellationToken ct = default);
}
