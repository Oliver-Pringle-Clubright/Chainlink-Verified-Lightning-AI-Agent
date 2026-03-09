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

        // 4. Update task status to Completed
        task.Status = TaskStatus.Completed;
        task.CompletedAt = DateTime.UtcNow;
        task.UpdatedAt = DateTime.UtcNow;
        await _taskRepo.UpdateStatusAsync(task.Id, TaskStatus.Completed, ct);

        _logger.LogInformation(
            "Orchestration completed for task {TaskId} with {SubtaskCount} subtasks",
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

        // Collect outputs from milestone VerificationResult fields
        var subtaskOutputs = new List<string>();

        foreach (var subtask in subtasks)
        {
            var milestones = await _milestoneRepo.GetByTaskIdAsync(subtask.Id, ct);
            foreach (var milestone in milestones)
            {
                if (!string.IsNullOrEmpty(milestone.VerificationResult))
                {
                    subtaskOutputs.Add(milestone.VerificationResult);
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
