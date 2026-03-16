using LightningAgentMarketPlace.Core.Models;

namespace LightningAgentMarketPlace.Core.Interfaces.Data;

public interface IAuditLogRepository
{
    Task<int> CreateAsync(AuditLogEntry entry, CancellationToken ct = default);
    Task<AuditLogEntry?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLogEntry>> GetByAgentAsync(int agentId, int limit = 100, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLogEntry>> GetByEntityAsync(string entityType, int entityId, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLogEntry>> GetRecentAsync(int count, CancellationToken ct = default);
    Task<(IReadOnlyList<AuditLogEntry> Items, int TotalCount)> GetPagedAsync(
        int offset, int limit, int? agentId = null, string? action = null,
        DateTime? startDate = null, DateTime? endDate = null, CancellationToken ct = default);
    Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct = default);
}
