using LightningAgent.AI.Orchestrator;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using LightningAgent.Core.Models.AI;
using Microsoft.Extensions.Logging;
using TaskStatus = LightningAgent.Core.Enums.TaskStatus;

namespace LightningAgent.Engine;

public class TaskOrchestrator : ITaskOrchestrator
{
    private readonly TaskDecompositionEngine _decompositionEngine;
    private readonly IAgentMatcher _agentMatcher;
    private readonly IEscrowManager _escrowManager;
    private readonly IVerificationPipeline _verificationPipeline;
    private readonly IPaymentService _paymentService;
    private readonly IReputationService _reputationService;
    private readonly ISpendLimitService _spendLimitService;
    private readonly IEventPublisher _eventPublisher;
    private readonly DeliverableAssembler _assembler;
    private readonly ITaskRepository _taskRepo;
    private readonly IMilestoneRepository _milestoneRepo;
    private readonly IVerificationRepository _verificationRepo;
    private readonly ILogger<TaskOrchestrator> _logger;

    public TaskOrchestrator(
        TaskDecompositionEngine decompositionEngine,
        IAgentMatcher agentMatcher,
        IEscrowManager escrowManager,
        IVerificationPipeline verificationPipeline,
        IPaymentService paymentService,
        IReputationService reputationService,
        ISpendLimitService spendLimitService,
        IEventPublisher eventPublisher,
        DeliverableAssembler assembler,
        ITaskRepository taskRepo,
        IMilestoneRepository milestoneRepo,
        IVerificationRepository verificationRepo,
        ILogger<TaskOrchestrator> logger)
    {
        _decompositionEngine = decompositionEngine;
        _agentMatcher = agentMatcher;
        _escrowManager = escrowManager;
        _verificationPipeline = verificationPipeline;
        _paymentService = paymentService;
        _reputationService = reputationService;
        _spendLimitService = spendLimitService;
        _eventPublisher = eventPublisher;
        _assembler = assembler;
        _taskRepo = taskRepo;
        _milestoneRepo = milestoneRepo;
        _verificationRepo = verificationRepo;
        _logger = logger;
    }

    public async Task<TaskItem> OrchestrateTaskAsync(TaskItem task, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Beginning orchestration for task {TaskId} '{Title}'",
            task.Id, task.Title);

        // 1. Update task status to InProgress
        task.Status = TaskStatus.InProgress;
        task.UpdatedAt = DateTime.UtcNow;
        await _taskRepo.UpdateStatusAsync(task.Id, TaskStatus.InProgress, ct);

        // 2. Decompose the task into subtasks and milestones
        OrchestrationPlan plan;
        try
        {
            plan = await _decompositionEngine.DecomposeAndCreateSubtasksAsync(task, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decompose task {TaskId}", task.Id);
            await _taskRepo.UpdateStatusAsync(task.Id, TaskStatus.Failed, ct);
            task.Status = TaskStatus.Failed;
            return task;
        }

        // 3. Process each subtask in dependency order
        var subtasks = await _taskRepo.GetSubtasksAsync(task.Id, ct);

        // Build a dependency-ordered sequence from the plan
        var orderedSubtasks = OrderByDependencies(subtasks, plan);

        foreach (var subtask in orderedSubtasks)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // a. Find best agent
                var candidates = await _agentMatcher.FindBestAgentAsync(subtask, ct);
                if (candidates.Count == 0)
                {
                    _logger.LogWarning(
                        "No matching agents found for subtask {SubtaskId} '{Title}'",
                        subtask.Id, subtask.Title);
                    continue;
                }

                var bestAgent = candidates[0].Agent;

                // b. Check spend limit
                var withinLimit = await _spendLimitService.CheckLimitAsync(
                    bestAgent.Id, subtask.MaxPayoutSats, ct);
                if (!withinLimit)
                {
                    _logger.LogWarning(
                        "Spend limit exceeded for agent {AgentId} on subtask {SubtaskId} ({Sats} sats)",
                        bestAgent.Id, subtask.Id, subtask.MaxPayoutSats);
                    continue;
                }

                // c. Assign agent to subtask
                subtask.AssignedAgentId = bestAgent.Id;
                subtask.Status = TaskStatus.Assigned;
                subtask.UpdatedAt = DateTime.UtcNow;
                await _taskRepo.UpdateAsync(subtask, ct);

                _logger.LogInformation(
                    "Assigned agent {AgentId} to subtask {SubtaskId} '{Title}'",
                    bestAgent.Id, subtask.Id, subtask.Title);

                await _eventPublisher.PublishTaskAssignedAsync(subtask.Id, bestAgent.Id, ct);

                // d. Create escrow for each milestone of this subtask
                var milestones = await _milestoneRepo.GetByTaskIdAsync(subtask.Id, ct);
                foreach (var milestone in milestones)
                {
                    var escrow = await _escrowManager.CreateEscrowAsync(milestone, ct);
                    _logger.LogInformation(
                        "Created escrow {EscrowId} for milestone {MilestoneId} ({Sats} sats)",
                        escrow.Id, milestone.Id, milestone.PayoutSats);
                }

                // e. In production, the agent would be notified and we would await output.
                //    For now, mark subtask as in progress (agent execution is external).
                subtask.Status = TaskStatus.InProgress;
                subtask.UpdatedAt = DateTime.UtcNow;
                await _taskRepo.UpdateStatusAsync(subtask.Id, TaskStatus.InProgress, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error processing subtask {SubtaskId} '{Title}'",
                    subtask.Id, subtask.Title);
            }
        }

        // 4. Task stays InProgress — it will be completed when all milestones pass verification
        _logger.LogInformation(
            "Orchestration setup completed for task {TaskId} with {SubtaskCount} subtasks. " +
            "Task remains InProgress until milestones are verified.",
            task.Id, subtasks.Count);

        return task;
    }

    public async Task<OrchestrationPlan> DecomposeTaskAsync(TaskItem task, CancellationToken ct = default)
    {
        _logger.LogInformation("Decomposing task {TaskId} '{Title}'", task.Id, task.Title);
        return await _decompositionEngine.DecomposeAndCreateSubtasksAsync(task, ct);
    }

    public async Task<string> AssembleDeliverableAsync(int taskId, CancellationToken ct = default)
    {
        _logger.LogInformation("Assembling deliverable for task {TaskId}", taskId);

        var task = await _taskRepo.GetByIdAsync(taskId, ct)
            ?? throw new InvalidOperationException($"Task {taskId} not found");

        // Get all subtasks for this task
        var subtasks = await _taskRepo.GetSubtasksAsync(taskId, ct);

        // Collect outputs from milestone OutputData fields (base64-decoded)
        var subtaskOutputs = new List<string>();

        foreach (var subtask in subtasks)
        {
            var milestones = await _milestoneRepo.GetByTaskIdAsync(subtask.Id, ct);
            foreach (var milestone in milestones)
            {
                if (!string.IsNullOrEmpty(milestone.OutputData))
                {
                    var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(milestone.OutputData));
                    subtaskOutputs.Add(decoded);
                }
            }
        }

        if (subtaskOutputs.Count == 0)
        {
            _logger.LogWarning("No subtask outputs found for task {TaskId}", taskId);
            return string.Empty;
        }

        var assembled = await _assembler.AssembleAsync(task.Description, subtaskOutputs, ct);

        _logger.LogInformation(
            "Deliverable assembled for task {TaskId} from {OutputCount} subtask outputs",
            taskId, subtaskOutputs.Count);

        return assembled;
    }

    public async Task<bool> CheckAndCompleteTaskAsync(int taskId, CancellationToken ct = default)
    {
        _logger.LogInformation("Checking completion status for task {TaskId}", taskId);

        var task = await _taskRepo.GetByIdAsync(taskId, ct);
        if (task is null)
        {
            _logger.LogWarning("Task {TaskId} not found during completion check", taskId);
            return false;
        }

        // Already in a terminal state
        if (task.Status is TaskStatus.Completed or TaskStatus.Failed)
        {
            return true;
        }

        // Collect all milestones for this task and its subtasks
        var allMilestones = new List<Milestone>();

        // Direct milestones on the task itself
        var taskMilestones = await _milestoneRepo.GetByTaskIdAsync(taskId, ct);
        allMilestones.AddRange(taskMilestones);

        // Milestones on subtasks
        var subtasks = await _taskRepo.GetSubtasksAsync(taskId, ct);
        foreach (var subtask in subtasks)
        {
            var subtaskMilestones = await _milestoneRepo.GetByTaskIdAsync(subtask.Id, ct);
            allMilestones.AddRange(subtaskMilestones);
        }

        if (allMilestones.Count == 0)
        {
            _logger.LogWarning("No milestones found for task {TaskId}", taskId);
            return false;
        }

        bool allPassed = allMilestones.All(m => m.Status == Core.Enums.MilestoneStatus.Passed);
        bool anyFailed = allMilestones.Any(m => m.Status == Core.Enums.MilestoneStatus.Failed);

        if (allPassed)
        {
            _logger.LogInformation(
                "All {Count} milestones passed for task {TaskId}. Marking as Completed.",
                allMilestones.Count, taskId);

            task.Status = TaskStatus.Completed;
            task.CompletedAt = DateTime.UtcNow;
            task.UpdatedAt = DateTime.UtcNow;
            await _taskRepo.UpdateStatusAsync(task.Id, TaskStatus.Completed, ct);

            // Also mark subtasks as completed
            foreach (var subtask in subtasks)
            {
                var stMilestones = await _milestoneRepo.GetByTaskIdAsync(subtask.Id, ct);
                if (stMilestones.All(m => m.Status == Core.Enums.MilestoneStatus.Passed))
                {
                    await _taskRepo.UpdateStatusAsync(subtask.Id, TaskStatus.Completed, ct);
                }
            }

            // Assemble final deliverable
            try
            {
                await AssembleDeliverableAsync(taskId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to assemble deliverable for task {TaskId}", taskId);
            }

            return true;
        }

        if (anyFailed)
        {
            // Check if any failed milestones still have no retries left
            // (if milestone is Failed, it means verification failed and was not retried)
            bool allFailedAreTerminal = allMilestones
                .Where(m => m.Status == Core.Enums.MilestoneStatus.Failed)
                .All(_ => true); // Failed milestones without retry are terminal

            // If there are still pending/in-progress milestones, the task is not terminal yet
            bool hasActiveMilestones = allMilestones.Any(m =>
                m.Status is Core.Enums.MilestoneStatus.Pending
                    or Core.Enums.MilestoneStatus.InProgress
                    or Core.Enums.MilestoneStatus.Verifying);

            if (!hasActiveMilestones && allFailedAreTerminal)
            {
                _logger.LogInformation(
                    "Task {TaskId} has failed milestones with no active work remaining. Marking as Failed.",
                    taskId);

                task.Status = TaskStatus.Failed;
                task.UpdatedAt = DateTime.UtcNow;
                await _taskRepo.UpdateStatusAsync(task.Id, TaskStatus.Failed, ct);

                return true;
            }
        }

        _logger.LogDebug(
            "Task {TaskId} not yet terminal. Milestones: {Passed} passed, {Failed} failed, {Other} pending/active.",
            taskId,
            allMilestones.Count(m => m.Status == Core.Enums.MilestoneStatus.Passed),
            allMilestones.Count(m => m.Status == Core.Enums.MilestoneStatus.Failed),
            allMilestones.Count(m => m.Status is Core.Enums.MilestoneStatus.Pending
                or Core.Enums.MilestoneStatus.InProgress
                or Core.Enums.MilestoneStatus.Verifying));

        return false;
    }

    /// <summary>
    /// Orders subtasks by their dependency chain from the orchestration plan.
    /// Subtasks with no dependencies come first; those that depend on earlier
    /// subtasks come later.
    /// </summary>
    private static List<TaskItem> OrderByDependencies(
        IReadOnlyList<TaskItem> subtasks,
        OrchestrationPlan plan)
    {
        if (plan.Subtasks.Count == 0)
            return subtasks.ToList();

        // Build a mapping from plan index to subtask entity
        // (subtasks are created in plan order, so index alignment works)
        var ordered = new List<TaskItem>();
        var visited = new HashSet<int>();
        var subtaskList = subtasks.ToList();

        void Visit(int planIndex)
        {
            if (planIndex < 0 || planIndex >= plan.Subtasks.Count) return;
            if (!visited.Add(planIndex)) return;

            var planned = plan.Subtasks[planIndex];
            if (planned.DependsOn is not null)
            {
                foreach (var dep in planned.DependsOn)
                {
                    Visit(dep);
                }
            }

            if (planIndex < subtaskList.Count)
            {
                ordered.Add(subtaskList[planIndex]);
            }
        }

        for (int i = 0; i < plan.Subtasks.Count; i++)
        {
            Visit(i);
        }

        // Append any subtasks not covered by the plan
        foreach (var subtask in subtaskList)
        {
            if (!ordered.Contains(subtask))
            {
                ordered.Add(subtask);
            }
        }

        return ordered;
    }
}
