using LightningAgentMarketPlace.Core.Models;
using LightningAgentMarketPlace.Core.Models.AI;

namespace LightningAgentMarketPlace.Core.Interfaces.Services;

public interface ITaskOrchestrator
{
    Task<TaskItem> OrchestrateTaskAsync(TaskItem task, CancellationToken ct = default);
    Task<OrchestrationPlan> DecomposeTaskAsync(TaskItem task, CancellationToken ct = default);
    Task<string> AssembleDeliverableAsync(int taskId, CancellationToken ct = default);

    /// <summary>
    /// Checks whether all milestones for the given task have reached a terminal state.
    /// If all milestones passed, marks the task as Completed and assembles the deliverable.
    /// If any milestone failed with no retries left, marks the task as Failed.
    /// </summary>
    /// <returns>True if the task reached a terminal state (Completed or Failed).</returns>
    Task<bool> CheckAndCompleteTaskAsync(int taskId, CancellationToken ct = default);
}
