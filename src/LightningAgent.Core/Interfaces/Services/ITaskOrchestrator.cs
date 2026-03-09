using LightningAgent.Core.Models;
using LightningAgent.Core.Models.AI;

namespace LightningAgent.Core.Interfaces.Services;

public interface ITaskOrchestrator
{
    Task<TaskItem> OrchestrateTaskAsync(TaskItem task, CancellationToken ct = default);
    Task<OrchestrationPlan> DecomposeTaskAsync(TaskItem task, CancellationToken ct = default);
    Task<string> AssembleDeliverableAsync(int taskId, CancellationToken ct = default);
}
