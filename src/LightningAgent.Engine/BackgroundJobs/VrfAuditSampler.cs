using System.Numerics;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LightningAgent.Engine.BackgroundJobs;

public class VrfAuditSampler : BackgroundService
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LookbackWindow = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VrfAuditSampler> _logger;

    public VrfAuditSampler(
        IServiceScopeFactory scopeFactory,
        ILogger<VrfAuditSampler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("VrfAuditSampler started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var taskRepo = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
                var milestoneRepo = scope.ServiceProvider.GetRequiredService<IMilestoneRepository>();
                var vrfClient = scope.ServiceProvider.GetRequiredService<IChainlinkVrfClient>();
                var fraudDetector = scope.ServiceProvider.GetRequiredService<IFraudDetector>();

                var since = DateTime.UtcNow.Subtract(LookbackWindow);
                var completedTasks = await taskRepo.GetCompletedSinceAsync(since, stoppingToken);

                if (completedTasks.Count == 0)
                {
                    _logger.LogDebug("VrfAuditSampler found no completed tasks in the last 24 hours, skipping");
                }
                else
                {
                    // Request a random number from Chainlink VRF
                    var vrfResult = await vrfClient.RequestRandomnessAsync(1, stoppingToken);

                    if (vrfResult.Randomness is null || vrfResult.Randomness.Count == 0)
                    {
                        _logger.LogWarning(
                            "VrfAuditSampler received no randomness from VRF (requestId={RequestId}), skipping",
                            vrfResult.RequestId);
                    }
                    else
                    {
                        // Use the random number to select a task for audit
                        var randomValue = BigInteger.Parse(vrfResult.Randomness[0]);
                        var index = (int)(BigInteger.Abs(randomValue) % completedTasks.Count);
                        var selectedTask = completedTasks[index];

                        _logger.LogInformation(
                            "VrfAuditSampler selected task {TaskId} ('{Title}') for audit via VRF (index={Index}/{Total})",
                            selectedTask.Id, selectedTask.Title, index, completedTasks.Count);

                        // Get milestones for the selected task and run fraud detection
                        var milestones = await milestoneRepo.GetByTaskIdAsync(selectedTask.Id, stoppingToken);

                        if (selectedTask.AssignedAgentId.HasValue)
                        {
                            var sybilReport = await fraudDetector.DetectSybilAsync(
                                selectedTask.AssignedAgentId.Value, stoppingToken);

                            if (sybilReport is not null)
                            {
                                _logger.LogWarning(
                                    "VrfAuditSampler SYBIL alert for agent {AgentId} on task {TaskId}: confidence={Confidence:F2}, action={Action}",
                                    selectedTask.AssignedAgentId.Value, selectedTask.Id,
                                    sybilReport.Confidence, sybilReport.RecommendedAction);
                            }

                            var anomalyScore = await fraudDetector.GetAnomalyScoreAsync(
                                selectedTask.AssignedAgentId.Value, stoppingToken);

                            if (anomalyScore > 0.7)
                            {
                                _logger.LogWarning(
                                    "VrfAuditSampler HIGH anomaly score {Score:F2} for agent {AgentId} on task {TaskId}",
                                    anomalyScore, selectedTask.AssignedAgentId.Value, selectedTask.Id);
                            }
                            else
                            {
                                _logger.LogInformation(
                                    "VrfAuditSampler anomaly score {Score:F2} for agent {AgentId} on task {TaskId} — within normal range",
                                    anomalyScore, selectedTask.AssignedAgentId.Value, selectedTask.Id);
                            }
                        }

                        _logger.LogInformation(
                            "VrfAuditSampler completed audit of task {TaskId} with {MilestoneCount} milestones",
                            selectedTask.Id, milestones.Count);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "VrfAuditSampler encountered an error during sample cycle (VRF may not be configured)");
            }

            try
            {
                await Task.Delay(SampleInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("VrfAuditSampler stopped");
    }
}
