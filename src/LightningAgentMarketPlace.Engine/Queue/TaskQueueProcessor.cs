using LightningAgentMarketPlace.Core.Interfaces.Data;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LightningAgentMarketPlace.Engine.Queue;

/// <summary>
/// Background service that continuously dequeues task IDs from <see cref="ITaskQueue"/>
/// and orchestrates each one inside a fresh DI scope.
/// </summary>
public class TaskQueueProcessor : BackgroundService
{
    private readonly ITaskQueue _taskQueue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TaskQueueProcessor> _logger;

    public TaskQueueProcessor(
        ITaskQueue taskQueue,
        IServiceScopeFactory scopeFactory,
        ILogger<TaskQueueProcessor> logger)
    {
        _taskQueue = taskQueue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TaskQueueProcessor started — waiting for queued tasks");

        while (!stoppingToken.IsCancellationRequested)
        {
            int taskId;
            try
            {
                taskId = await _taskQueue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            _logger.LogInformation("TaskQueueProcessor: dequeued task {TaskId} for orchestration", taskId);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var taskRepo = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
                var orchestrator = scope.ServiceProvider.GetRequiredService<ITaskOrchestrator>();

                var task = await taskRepo.GetByIdAsync(taskId, stoppingToken);
                if (task is null)
                {
                    _logger.LogWarning(
                        "TaskQueueProcessor: task {TaskId} not found in repository, skipping",
                        taskId);
                    continue;
                }

                await orchestrator.OrchestrateTaskAsync(task, stoppingToken);

                _logger.LogInformation(
                    "TaskQueueProcessor: orchestration completed for task {TaskId}",
                    taskId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "TaskQueueProcessor: error orchestrating task {TaskId}",
                    taskId);
            }
        }

        _logger.LogInformation("TaskQueueProcessor stopped");
    }
}
