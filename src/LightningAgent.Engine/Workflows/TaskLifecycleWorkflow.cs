using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using Microsoft.Extensions.Logging;
using VerificationEntity = LightningAgent.Core.Models.Verification;

namespace LightningAgent.Engine.Workflows;

public class TaskLifecycleWorkflow
{
    private readonly ITaskRepository _taskRepo;
    private readonly IMilestoneRepository _milestoneRepo;
    private readonly IVerificationPipeline _verificationPipeline;
    private readonly IVerificationRepository _verificationRepo;
    private readonly IEscrowManager _escrowManager;
    private readonly IEscrowRepository _escrowRepo;
    private readonly IPaymentService _paymentService;
    private readonly IReputationService _reputationService;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<TaskLifecycleWorkflow> _logger;

    /// <summary>
    /// Minimum average score threshold for a milestone to be considered passed
    /// when not all individual strategies report a pass.
    /// </summary>
    private const double PassThreshold = 0.7;

    public TaskLifecycleWorkflow(
        ITaskRepository taskRepo,
        IMilestoneRepository milestoneRepo,
        IVerificationPipeline verificationPipeline,
        IVerificationRepository verificationRepo,
        IEscrowManager escrowManager,
        IEscrowRepository escrowRepo,
        IPaymentService paymentService,
        IReputationService reputationService,
        IEventPublisher eventPublisher,
        ILogger<TaskLifecycleWorkflow> logger)
    {
        _taskRepo = taskRepo;
        _milestoneRepo = milestoneRepo;
        _verificationPipeline = verificationPipeline;
        _verificationRepo = verificationRepo;
        _escrowManager = escrowManager;
        _escrowRepo = escrowRepo;
        _paymentService = paymentService;
        _reputationService = reputationService;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    /// <summary>
    /// Processes an agent's submission for a milestone: verifies the output,
    /// and either settles escrow + pays the agent, or fails the milestone
    /// and cancels the escrow.
    /// </summary>
    /// <returns>True if the milestone passed verification; false otherwise.</returns>
    public async Task<bool> ProcessMilestoneSubmissionAsync(
        int milestoneId,
        byte[] agentOutput,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Processing submission for milestone {MilestoneId} ({OutputBytes} bytes)",
            milestoneId, agentOutput.Length);

        // 1. Get milestone from DB
        var milestone = await _milestoneRepo.GetByIdAsync(milestoneId, ct)
            ?? throw new InvalidOperationException($"Milestone {milestoneId} not found");

        // 2. Update status to Verifying
        milestone.Status = MilestoneStatus.Verifying;
        await _milestoneRepo.UpdateAsync(milestone, ct);

        // 3. Run verification pipeline
        var results = await _verificationPipeline.RunVerificationAsync(milestone, agentOutput, ct);

        // 4. Save each VerificationResult to DB
        foreach (var result in results)
        {
            var verification = new VerificationEntity
            {
                MilestoneId = milestone.Id,
                TaskId = milestone.TaskId,
                StrategyType = result.StrategyType,
                Score = result.Score,
                Passed = result.Passed,
                Details = result.Details,
                CreatedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            };

            await _verificationRepo.CreateAsync(verification, ct);
        }

        // 5. Determine overall pass/fail
        bool allPassed = results.Count > 0 && results.All(r => r.Passed);
        double averageScore = results.Count > 0 ? results.Average(r => r.Score) : 0.0;
        bool passed = allPassed || averageScore >= PassThreshold;

        _logger.LogInformation(
            "Milestone {MilestoneId} verification: allPassed={AllPassed}, avgScore={AvgScore:F3}, result={Result}",
            milestoneId, allPassed, averageScore, passed ? "PASSED" : "FAILED");

        // Get the task to find assigned agent
        var task = await _taskRepo.GetByIdAsync(milestone.TaskId, ct);
        int agentId = task?.AssignedAgentId ?? 0;

        if (passed)
        {
            await _eventPublisher.PublishMilestoneVerifiedAsync(milestoneId, milestone.TaskId, true, averageScore, ct);

            // 6a. Update milestone status to Passed
            milestone.Status = MilestoneStatus.Passed;
            milestone.VerifiedAt = DateTime.UtcNow;
            milestone.VerificationResult = string.Join("; ",
                results.Select(r => $"{r.StrategyType}: {r.Score:F2} ({(r.Passed ? "pass" : "fail")})"));
            await _milestoneRepo.UpdateAsync(milestone, ct);

            // 6b. Settle escrow
            var escrow = await _escrowRepo.GetByMilestoneIdAsync(milestoneId, ct);
            if (escrow is not null)
            {
                var preimage = Convert.FromHexString(escrow.PaymentPreimage ?? string.Empty);
                await _escrowManager.SettleEscrowAsync(escrow.Id, preimage, ct);

                _logger.LogInformation(
                    "Escrow {EscrowId} settled for milestone {MilestoneId}",
                    escrow.Id, milestoneId);
            }

            // 6c. Process payment
            var payment = await _paymentService.ProcessMilestonePaymentAsync(milestoneId, ct);
            _logger.LogInformation(
                "Payment {PaymentId} processed for milestone {MilestoneId}",
                payment.Id, milestoneId);

            // 6d. Update reputation
            if (agentId > 0)
            {
                await _reputationService.UpdateReputationAsync(
                    agentId,
                    taskCompleted: true,
                    verificationPassed: true,
                    responseTimeSec: 0.0,
                    ct);
            }
        }
        else
        {
            var failReason = string.Join("; ",
                results.Where(r => !r.Passed).Select(r => $"{r.StrategyType}: {r.Score:F2}"));
            await _eventPublisher.PublishVerificationFailedAsync(milestoneId, milestone.TaskId, failReason, ct);

            // 7a. Update milestone status to Failed
            milestone.Status = MilestoneStatus.Failed;
            milestone.VerificationResult = string.Join("; ",
                results.Select(r => $"{r.StrategyType}: {r.Score:F2} ({(r.Passed ? "pass" : "fail")})"));
            await _milestoneRepo.UpdateAsync(milestone, ct);

            // 7b. Cancel escrow
            var escrow = await _escrowRepo.GetByMilestoneIdAsync(milestoneId, ct);
            if (escrow is not null)
            {
                await _escrowManager.CancelEscrowAsync(escrow.Id, ct);

                _logger.LogInformation(
                    "Escrow {EscrowId} cancelled for failed milestone {MilestoneId}",
                    escrow.Id, milestoneId);
            }

            // 7c. Update reputation
            if (agentId > 0)
            {
                await _reputationService.UpdateReputationAsync(
                    agentId,
                    taskCompleted: false,
                    verificationPassed: false,
                    responseTimeSec: 0.0,
                    ct);
            }
        }

        return passed;
    }

    /// <summary>
    /// Resets a failed milestone for retry: sets status back to Pending
    /// and creates a new escrow.
    /// </summary>
    public async Task ProcessRetryAsync(int milestoneId, CancellationToken ct = default)
    {
        _logger.LogInformation("Processing retry for milestone {MilestoneId}", milestoneId);

        var milestone = await _milestoneRepo.GetByIdAsync(milestoneId, ct)
            ?? throw new InvalidOperationException($"Milestone {milestoneId} not found");

        // 1. Reset milestone status to Pending
        milestone.Status = MilestoneStatus.Pending;
        milestone.VerifiedAt = null;
        milestone.VerificationResult = null;
        await _milestoneRepo.UpdateAsync(milestone, ct);

        // 2. Create new escrow
        var escrow = await _escrowManager.CreateEscrowAsync(milestone, ct);

        _logger.LogInformation(
            "Milestone {MilestoneId} reset for retry with new escrow {EscrowId}",
            milestoneId, escrow.Id);
    }
}
