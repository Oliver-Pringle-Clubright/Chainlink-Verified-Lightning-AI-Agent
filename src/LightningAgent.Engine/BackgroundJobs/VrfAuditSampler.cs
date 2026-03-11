using System.Numerics;
using LightningAgent.Core.Configuration;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
                var verificationPipeline = scope.ServiceProvider.GetRequiredService<IVerificationPipeline>();
                var fraudDetector = scope.ServiceProvider.GetRequiredService<IFraudDetector>();
                var chainlinkSettings = scope.ServiceProvider.GetRequiredService<IOptions<ChainlinkSettings>>().Value;

                var since = DateTime.UtcNow.Subtract(LookbackWindow);
                var completedTasks = await taskRepo.GetCompletedSinceAsync(since, stoppingToken);

                if (completedTasks.Count == 0)
                {
                    _logger.LogDebug("VrfAuditSampler found no completed tasks in the last 24 hours, skipping");
                }
                else
                {
                    int taskIndex;
                    bool usedVrf = false;

                    // Determine task selection index using VRF or fallback
                    if (!string.IsNullOrEmpty(chainlinkSettings.VrfCoordinatorAddress))
                    {
                        try
                        {
                            var vrfResult = await vrfClient.RequestRandomnessAsync(1, stoppingToken);

                            if (vrfResult.Randomness is not null && vrfResult.Randomness.Count > 0)
                            {
                                var randomValue = BigInteger.Parse(vrfResult.Randomness[0]);
                                taskIndex = (int)(BigInteger.Abs(randomValue) % completedTasks.Count);
                                usedVrf = true;
                                _logger.LogInformation(
                                    "VrfAuditSampler using VRF randomness for task selection (requestId={RequestId})",
                                    vrfResult.RequestId);
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "VrfAuditSampler received no randomness from VRF (requestId={RequestId}), falling back to Random",
                                    vrfResult.RequestId);
                                taskIndex = Random.Shared.Next(completedTasks.Count);
                            }
                        }
                        catch (Exception vrfEx)
                        {
                            _logger.LogWarning(
                                vrfEx,
                                "VrfAuditSampler failed to request VRF randomness, falling back to Random");
                            taskIndex = Random.Shared.Next(completedTasks.Count);
                        }
                    }
                    else
                    {
                        _logger.LogDebug("VrfAuditSampler VRF coordinator not configured, using Random fallback");
                        taskIndex = Random.Shared.Next(completedTasks.Count);
                    }

                    var selectedTask = completedTasks[taskIndex];

                    _logger.LogInformation(
                        "VrfAuditSampler selected task {TaskId} ('{Title}') for audit (index={Index}/{Total}, vrf={UsedVrf})",
                        selectedTask.Id, selectedTask.Title, taskIndex, completedTasks.Count, usedVrf);

                    // Get milestones for the selected task
                    var milestones = await milestoneRepo.GetByTaskIdAsync(selectedTask.Id, stoppingToken);

                    // Re-run verification on a random milestone
                    var passedMilestones = milestones
                        .Where(m => m.Status == MilestoneStatus.Passed && m.OutputData is not null)
                        .ToList();

                    if (passedMilestones.Count > 0)
                    {
                        var milestoneIndex = Random.Shared.Next(passedMilestones.Count);
                        var auditMilestone = passedMilestones[milestoneIndex];

                        _logger.LogInformation(
                            "VrfAuditSampler re-verifying milestone {MilestoneId} ('{Title}') on task {TaskId}",
                            auditMilestone.Id, auditMilestone.Title, selectedTask.Id);

                        try
                        {
                            var outputData = Convert.FromBase64String(auditMilestone.OutputData!);
                            var reVerifyResults = await verificationPipeline.RunVerificationAsync(
                                auditMilestone, outputData, stoppingToken);

                            var allPassed = reVerifyResults.Count > 0 && reVerifyResults.All(r => r.Passed);
                            var avgScore = reVerifyResults.Count > 0 ? reVerifyResults.Average(r => r.Score) : 0.0;

                            if (allPassed)
                            {
                                _logger.LogInformation(
                                    "VrfAuditSampler re-verification PASSED for milestone {MilestoneId} (avgScore={AvgScore:F3})",
                                    auditMilestone.Id, avgScore);
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "VrfAuditSampler re-verification DISCREPANCY for milestone {MilestoneId} (avgScore={AvgScore:F3}, allPassed={AllPassed})",
                                    auditMilestone.Id, avgScore, allPassed);
                            }
                        }
                        catch (Exception reVerifyEx)
                        {
                            _logger.LogWarning(
                                reVerifyEx,
                                "VrfAuditSampler failed to re-verify milestone {MilestoneId}",
                                auditMilestone.Id);
                        }
                    }
                    else
                    {
                        _logger.LogDebug(
                            "VrfAuditSampler no passed milestones with output data for task {TaskId}, skipping re-verification",
                            selectedTask.Id);
                    }

                    // Run fraud detection on the assigned agent
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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "VrfAuditSampler encountered an error during sample cycle");
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
