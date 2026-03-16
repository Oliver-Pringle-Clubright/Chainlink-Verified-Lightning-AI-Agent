using LightningAgentMarketPlace.Core.Enums;
using LightningAgentMarketPlace.Core.Interfaces.Data;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskStatus = LightningAgentMarketPlace.Core.Enums.TaskStatus;

namespace LightningAgentMarketPlace.Engine.BackgroundJobs;

/// <summary>
/// Detects tasks stuck in Assigned or InProgress status for too long and
/// resets them to Pending so they can be picked up by another agent.
/// Prevents task orphaning when an assigned agent becomes unresponsive.
/// </summary>
public class StaleTaskReassigner : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StalenessThreshold = TimeSpan.FromHours(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StaleTaskReassigner> _logger;
    private readonly IServiceHealthTracker _healthTracker;

    public StaleTaskReassigner(
        IServiceScopeFactory scopeFactory,
        ILogger<StaleTaskReassigner> logger,
        IServiceHealthTracker healthTracker)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _healthTracker = healthTracker;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StaleTaskReassigner started (threshold={Threshold})", StalenessThreshold);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckForStaleTasksAsync(stoppingToken);
                _healthTracker.RecordSuccess("StaleTaskReassigner");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _healthTracker.RecordFailure("StaleTaskReassigner", ex.Message);
                _logger.LogError(ex, "StaleTaskReassigner encountered an error");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("StaleTaskReassigner stopped");
    }

    private async Task CheckForStaleTasksAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var taskRepo = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        var escrowRepo = scope.ServiceProvider.GetRequiredService<IEscrowRepository>();
        var escrowManager = scope.ServiceProvider.GetRequiredService<IEscrowManager>();

        var cutoff = DateTime.UtcNow - StalenessThreshold;
        var reassignedCount = 0;

        // Check both Assigned and InProgress tasks
        foreach (var status in new[] { TaskStatus.Assigned, TaskStatus.InProgress })
        {
            var tasks = await taskRepo.GetByStatusAsync(status, ct);

            foreach (var task in tasks)
            {
                if (task.UpdatedAt >= cutoff)
                    continue;

                var staleDuration = DateTime.UtcNow - task.UpdatedAt;

                _logger.LogWarning(
                    "Task {TaskId} '{Title}' is stale (status={Status}, agent={AgentId}, " +
                    "last updated {Duration:hh\\:mm} ago). Resetting to Pending.",
                    task.Id, task.Title, task.Status, task.AssignedAgentId, staleDuration);

                // Cancel any held escrows for this task's milestones
                try
                {
                    var milestoneRepo = scope.ServiceProvider.GetRequiredService<IMilestoneRepository>();
                    var milestones = await milestoneRepo.GetByTaskIdAsync(task.Id, ct);

                    foreach (var milestone in milestones)
                    {
                        var escrow = await escrowRepo.GetByMilestoneIdAsync(milestone.Id, ct);
                        if (escrow is not null && escrow.Status == EscrowStatus.Held)
                        {
                            await escrowManager.CancelEscrowAsync(escrow.Id, ct);
                            _logger.LogInformation(
                                "Cancelled held escrow {EscrowId} for stale task {TaskId} milestone {MilestoneId}",
                                escrow.Id, task.Id, milestone.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to cancel escrows for stale task {TaskId}", task.Id);
                }

                // Reset the task to Pending and clear the agent assignment
                task.AssignedAgentId = null;
                task.Status = TaskStatus.Pending;
                task.UpdatedAt = DateTime.UtcNow;
                await taskRepo.UpdateAsync(task, ct);

                reassignedCount++;
            }
        }

        if (reassignedCount > 0)
        {
            _logger.LogInformation(
                "StaleTaskReassigner reset {Count} stale task(s) to Pending",
                reassignedCount);
        }
    }
}
