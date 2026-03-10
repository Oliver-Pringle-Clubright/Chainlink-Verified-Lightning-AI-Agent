using LightningAgent.AI.Orchestrator;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using LightningAgent.Core.Models.AI;
using Microsoft.Extensions.Logging;
using TaskStatus = LightningAgent.Core.Enums.TaskStatus;

namespace LightningAgent.Engine;

public class TaskDecompositionEngine
{
    private readonly TaskDecomposer _decomposer;
    private readonly ITaskRepository _taskRepo;
    private readonly IMilestoneRepository _milestoneRepo;
    private readonly ILogger<TaskDecompositionEngine> _logger;

    public TaskDecompositionEngine(
        TaskDecomposer decomposer,
        ITaskRepository taskRepo,
        IMilestoneRepository milestoneRepo,
        ILogger<TaskDecompositionEngine> logger)
    {
        _decomposer = decomposer;
        _taskRepo = taskRepo;
        _milestoneRepo = milestoneRepo;
        _logger = logger;
    }

    public async Task<OrchestrationPlan> DecomposeAndCreateSubtasksAsync(
        TaskItem parentTask,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Decomposing task {TaskId} '{Title}' into subtasks (budget={BudgetSats} sats)",
            parentTask.Id, parentTask.Title, parentTask.MaxPayoutSats);

        var plan = await _decomposer.DecomposeAsync(
            parentTask.Title,
            parentTask.Description,
            parentTask.MaxPayoutSats,
            ct);

        plan.OriginalTaskId = parentTask.Id.ToString();

        _logger.LogInformation(
            "Task {TaskId} decomposed into {Count} subtasks, estimated total: {TotalSats} sats",
            parentTask.Id, plan.Subtasks.Count, plan.EstimatedTotalSats);

        for (int i = 0; i < plan.Subtasks.Count; i++)
        {
            var planned = plan.Subtasks[i];

            // Create the subtask entity
            var subtask = new TaskItem
            {
                ExternalId = Guid.NewGuid().ToString("N"),
                ParentTaskId = parentTask.Id,
                ClientId = parentTask.ClientId,
                Title = planned.Title,
                Description = planned.Description,
                TaskType = planned.TaskType,
                Status = TaskStatus.Pending,
                VerificationCriteria = planned.VerificationCriteria,
                MaxPayoutSats = planned.EstimatedSats,
                Priority = parentTask.Priority,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            subtask.Id = await _taskRepo.CreateAsync(subtask, ct);

            _logger.LogInformation(
                "Created subtask {SubtaskId} '{Title}' for parent {ParentId} (seq={Seq}, sats={Sats})",
                subtask.Id, subtask.Title, parentTask.Id, i + 1, planned.EstimatedSats);

            // Create a milestone for this subtask
            var milestone = new Milestone
            {
                TaskId = subtask.Id,
                SequenceNumber = i + 1,
                Title = planned.Title,
                Description = planned.Description,
                VerificationCriteria = planned.VerificationCriteria,
                PayoutSats = planned.EstimatedSats,
                Status = MilestoneStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            milestone.Id = await _milestoneRepo.CreateAsync(milestone, ct);

            _logger.LogInformation(
                "Created milestone {MilestoneId} for subtask {SubtaskId} (seq={Seq})",
                milestone.Id, subtask.Id, i + 1);
        }

        return plan;
    }
}
