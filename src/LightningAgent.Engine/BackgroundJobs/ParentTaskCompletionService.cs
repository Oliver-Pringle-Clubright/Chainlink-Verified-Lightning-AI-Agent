using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskStatus = LightningAgent.Core.Enums.TaskStatus;

namespace LightningAgent.Engine.BackgroundJobs;

/// <summary>
/// Background service that periodically checks for parent tasks where all subtasks
/// have completed, and marks the parent as Completed.
/// </summary>
public class ParentTaskCompletionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ParentTaskCompletionService> _logger;

    public ParentTaskCompletionService(
        IServiceScopeFactory scopeFactory,
        ILogger<ParentTaskCompletionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ParentTaskCompletionService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException) { break; }

            try
            {
                _logger.LogInformation("ParentTaskCompletionService: running sweep cycle");
                using var scope = _scopeFactory.CreateScope();
                var taskRepo = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
                var orchestrator = scope.ServiceProvider.GetRequiredService<ITaskOrchestrator>();

                var inProgressTasks = await taskRepo.GetByStatusAsync(TaskStatus.InProgress, stoppingToken);

                foreach (var task in inProgressTasks)
                {
                    var subtasks = await taskRepo.GetSubtasksAsync(task.Id, stoppingToken);
                    if (subtasks.Count > 0)
                    {
                        var statuses = subtasks.GroupBy(s => s.Status).Select(g => $"{g.Key}:{g.Count()}");
                        _logger.LogInformation("Task {TaskId}: {Count} subtasks [{Statuses}]",
                            task.Id, subtasks.Count, string.Join(", ", statuses));
                    }
                    if (subtasks.Count > 0 && subtasks.All(s =>
                        s.Status is TaskStatus.Completed or TaskStatus.Failed))
                    {
                        _logger.LogInformation(
                            "All subtasks done for parent task {TaskId} — marking complete", task.Id);
                        await orchestrator.CheckAndCompleteTaskAsync(task.Id, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ParentTaskCompletionService error");
            }
        }

        _logger.LogInformation("ParentTaskCompletionService stopped");
    }
}
