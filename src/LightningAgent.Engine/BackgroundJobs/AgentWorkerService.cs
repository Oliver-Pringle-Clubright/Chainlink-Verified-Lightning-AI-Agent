using LightningAgent.Core.Configuration;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightningAgent.Engine.BackgroundJobs;

/// <summary>
/// Background service that periodically polls for active agents with assigned tasks
/// and runs the WorkerAgent execution loop for each one.
/// </summary>
public class AgentWorkerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentWorkerService> _logger;
    private readonly WorkerAgentSettings _settings;

    public AgentWorkerService(
        IServiceScopeFactory scopeFactory,
        IOptions<WorkerAgentSettings> settings,
        ILogger<AgentWorkerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("AgentWorkerService is disabled via configuration");
            return;
        }

        var pollInterval = TimeSpan.FromSeconds(_settings.PollIntervalSeconds);
        _logger.LogInformation(
            "AgentWorkerService started (poll interval: {Interval}s)",
            _settings.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var agentRepo = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
                var workerAgent = scope.ServiceProvider.GetRequiredService<WorkerAgent>();

                // Get all active agents
                var activeAgents = await agentRepo.GetAllAsync(AgentStatus.Active, stoppingToken);

                using var semaphore = new SemaphoreSlim(_settings.MaxConcurrentAgents);

                var agentTasks = activeAgents.Select(async agent =>
                {
                    await semaphore.WaitAsync(stoppingToken);
                    try
                    {
                        await workerAgent.ExecuteAssignedWorkAsync(agent.Id, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        // Let cancellation propagate
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "AgentWorkerService: error running worker for agent {AgentId} '{Name}'",
                            agent.Id, agent.Name);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(agentTasks);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AgentWorkerService encountered an error during poll cycle");
            }

            try
            {
                await Task.Delay(pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("AgentWorkerService stopped");
    }
}
