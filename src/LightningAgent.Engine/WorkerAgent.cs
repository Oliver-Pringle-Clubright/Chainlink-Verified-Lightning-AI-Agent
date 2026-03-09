using System.Text;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Engine.Workflows;
using Microsoft.Extensions.Logging;
using TaskStatus = LightningAgent.Core.Enums.TaskStatus;

namespace LightningAgent.Engine;

/// <summary>
/// Executes assigned work for a given agent by processing pending milestones
/// through the Claude AI client and submitting outputs for verification.
/// </summary>
public class WorkerAgent
{
    private readonly IClaudeAiClient _aiClient;
    private readonly ITaskRepository _taskRepo;
    private readonly IMilestoneRepository _milestoneRepo;
    private readonly TaskLifecycleWorkflow _lifecycle;
    private readonly ITaskOrchestrator _orchestrator;
    private readonly ILogger<WorkerAgent> _logger;

    public WorkerAgent(
        IClaudeAiClient aiClient,
        ITaskRepository taskRepo,
        IMilestoneRepository milestoneRepo,
        TaskLifecycleWorkflow lifecycle,
        ITaskOrchestrator orchestrator,
        ILogger<WorkerAgent> logger)
    {
        _aiClient = aiClient;
        _taskRepo = taskRepo;
        _milestoneRepo = milestoneRepo;
        _lifecycle = lifecycle;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    /// <summary>
    /// Processes all assigned tasks and their pending milestones for a given agent.
    /// </summary>
    public async Task ExecuteAssignedWorkAsync(int agentId, CancellationToken ct = default)
    {
        _logger.LogInformation("WorkerAgent starting work for agent {AgentId}", agentId);

        // 1. Get all tasks assigned to this agent with status Assigned or InProgress
        var allAgentTasks = await _taskRepo.GetByAssignedAgentAsync(agentId, ct);
        var activeTasks = allAgentTasks
            .Where(t => t.Status is TaskStatus.Assigned or TaskStatus.InProgress)
            .ToList();

        if (activeTasks.Count == 0)
        {
            _logger.LogDebug("WorkerAgent: no active tasks for agent {AgentId}", agentId);
            return;
        }

        _logger.LogInformation(
            "WorkerAgent: agent {AgentId} has {Count} active tasks",
            agentId, activeTasks.Count);

        foreach (var task in activeTasks)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await ProcessTaskAsync(agentId, task, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "WorkerAgent: error processing task {TaskId} for agent {AgentId}",
                    task.Id, agentId);
            }
        }
    }

    private async Task ProcessTaskAsync(int agentId, Core.Models.TaskItem task, CancellationToken ct)
    {
        // If task is still Assigned, move it to InProgress
        if (task.Status == TaskStatus.Assigned)
        {
            await _taskRepo.UpdateStatusAsync(task.Id, TaskStatus.InProgress, ct);
        }

        // 2. Get milestones with status Pending or InProgress
        var milestones = await _milestoneRepo.GetByTaskIdAsync(task.Id, ct);
        var actionableMilestones = milestones
            .Where(m => m.Status is MilestoneStatus.Pending or MilestoneStatus.InProgress)
            .OrderBy(m => m.SequenceNumber)
            .ToList();

        if (actionableMilestones.Count == 0)
        {
            _logger.LogDebug(
                "WorkerAgent: no actionable milestones for task {TaskId}",
                task.Id);

            // Check if the task can be completed
            await _orchestrator.CheckAndCompleteTaskAsync(task.Id, ct);
            return;
        }

        foreach (var milestone in actionableMilestones)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // 3a. Update milestone status to InProgress
                if (milestone.Status == MilestoneStatus.Pending)
                {
                    await _milestoneRepo.UpdateStatusAsync(milestone.Id, MilestoneStatus.InProgress, ct);
                }

                // 3b. Build a prompt from the task description + milestone description + verification criteria
                var prompt = BuildPrompt(task, milestone);

                _logger.LogInformation(
                    "WorkerAgent: generating output for milestone {MilestoneId} (task {TaskId})",
                    milestone.Id, task.Id);

                // 3c. Call Claude AI to generate the output
                var systemPrompt =
                    "You are an AI agent executing a task milestone. " +
                    "Produce the requested output. Be thorough, precise, and follow the verification criteria exactly.";

                var aiOutput = await _aiClient.SendMessageAsync(systemPrompt, prompt, ct);

                // 3d. Convert output to bytes
                var outputBytes = Encoding.UTF8.GetBytes(aiOutput);

                // 3e. Call TaskLifecycleWorkflow.ProcessMilestoneSubmissionAsync to verify and pay
                var passed = await _lifecycle.ProcessMilestoneSubmissionAsync(milestone.Id, outputBytes, ct);

                // 3f. Log the result
                _logger.LogInformation(
                    "WorkerAgent: milestone {MilestoneId} (task {TaskId}) result: {Result}",
                    milestone.Id, task.Id, passed ? "PASSED" : "FAILED");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "WorkerAgent: error processing milestone {MilestoneId} (task {TaskId})",
                    milestone.Id, task.Id);
            }
        }

        // 4. After processing all milestones, check if the task is complete
        await _orchestrator.CheckAndCompleteTaskAsync(task.Id, ct);
    }

    private static string BuildPrompt(Core.Models.TaskItem task, Core.Models.Milestone milestone)
    {
        var sb = new StringBuilder();

        sb.AppendLine("## Task");
        sb.AppendLine($"**Title:** {task.Title}");
        sb.AppendLine($"**Description:** {task.Description}");
        sb.AppendLine();

        sb.AppendLine("## Milestone");
        sb.AppendLine($"**Title:** {milestone.Title}");
        if (!string.IsNullOrWhiteSpace(milestone.Description))
        {
            sb.AppendLine($"**Description:** {milestone.Description}");
        }
        sb.AppendLine();

        sb.AppendLine("## Verification Criteria");
        sb.AppendLine(milestone.VerificationCriteria);
        if (!string.IsNullOrWhiteSpace(task.VerificationCriteria))
        {
            sb.AppendLine();
            sb.AppendLine("### Overall Task Verification");
            sb.AppendLine(task.VerificationCriteria);
        }
        sb.AppendLine();

        sb.AppendLine("## Instructions");
        sb.AppendLine("Produce the output that satisfies the milestone description and verification criteria above.");
        sb.AppendLine("Return ONLY the deliverable content — no meta-commentary.");

        return sb.ToString();
    }
}
