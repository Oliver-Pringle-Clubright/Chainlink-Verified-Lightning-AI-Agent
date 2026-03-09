using LightningAgent.Core.Models;
using TaskStatus = LightningAgent.Core.Enums.TaskStatus;

namespace LightningAgent.Core.Interfaces.Data;

public interface ITaskRepository
{
    Task<TaskItem?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<TaskItem?> GetByExternalIdAsync(string externalId, CancellationToken ct = default);
    Task<IReadOnlyList<TaskItem>> GetByStatusAsync(TaskStatus status, CancellationToken ct = default);
    Task<IReadOnlyList<TaskItem>> GetByAssignedAgentAsync(int agentId, CancellationToken ct = default);
    Task<IReadOnlyList<TaskItem>> GetSubtasksAsync(int parentTaskId, CancellationToken ct = default);
    Task<IReadOnlyList<TaskItem>> GetCompletedSinceAsync(DateTime since, CancellationToken ct = default);
    Task<int> CreateAsync(TaskItem task, CancellationToken ct = default);
    Task UpdateAsync(TaskItem task, CancellationToken ct = default);
    Task UpdateStatusAsync(int id, TaskStatus status, CancellationToken ct = default);
}
