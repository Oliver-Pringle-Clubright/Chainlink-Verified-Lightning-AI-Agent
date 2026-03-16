using LightningAgentMarketPlace.Core.Models;
using TaskStatus = LightningAgentMarketPlace.Core.Enums.TaskStatus;

namespace LightningAgentMarketPlace.Core.Interfaces.Data;

public interface ITaskRepository
{
    Task<TaskItem?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<TaskItem?> GetByExternalIdAsync(string externalId, CancellationToken ct = default);
    Task<IReadOnlyList<TaskItem>> GetByStatusAsync(TaskStatus status, CancellationToken ct = default);
    Task<IReadOnlyList<TaskItem>> GetByAssignedAgentAsync(int agentId, CancellationToken ct = default);
    Task<IReadOnlyList<TaskItem>> GetSubtasksAsync(int parentTaskId, CancellationToken ct = default);
    Task<IReadOnlyList<TaskItem>> GetCompletedSinceAsync(DateTime since, CancellationToken ct = default);
    Task<int> GetCountAsync(TaskStatus? status = null, CancellationToken ct = default);
    Task<IReadOnlyList<TaskItem>> GetPagedAsync(int offset, int limit, TaskStatus? status = null, CancellationToken ct = default);

    /// <summary>
    /// Returns the count of tasks matching the optional filters.
    /// All filter parameters are combined with AND.
    /// </summary>
    Task<int> GetFilteredCountAsync(TaskStatus? status = null, int? agentId = null, string? clientId = null, CancellationToken ct = default);

    /// <summary>
    /// Returns a page of tasks matching the optional filters.
    /// Supports both offset and keyset (cursor-based) pagination.
    /// When <paramref name="cursor"/> is provided, keyset pagination is used (WHERE Id &lt; @cursor ORDER BY Id DESC);
    /// otherwise classic OFFSET/LIMIT is used.
    /// </summary>
    Task<IReadOnlyList<TaskItem>> GetFilteredPagedAsync(
        int offset,
        int limit,
        TaskStatus? status = null,
        int? agentId = null,
        string? clientId = null,
        int? cursor = null,
        CancellationToken ct = default);

    /// <summary>
    /// Full-text search across task titles and descriptions.
    /// </summary>
    Task<(IReadOnlyList<TaskItem> Items, int TotalCount)> SearchAsync(
        string query,
        int offset,
        int limit,
        TaskStatus? status = null,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves multiple tasks by their IDs in a single query.
    /// </summary>
    Task<IReadOnlyList<TaskItem>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken ct = default);

    Task<int> CreateAsync(TaskItem task, CancellationToken ct = default);
    Task UpdateAsync(TaskItem task, CancellationToken ct = default);
    Task UpdateStatusAsync(int id, TaskStatus status, CancellationToken ct = default);
}
