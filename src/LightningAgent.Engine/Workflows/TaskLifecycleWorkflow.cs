using System.Numerics;
using System.Security.Cryptography;
using LightningAgent.Core.Configuration;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using LightningAgent.Core.Models.Chainlink;
using LightningAgent.Engine.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly IChainlinkFunctionsClient _chainlinkFunctions;
    private readonly OnChainReputationService _onChainReputation;
    private readonly ChainlinkSettings _chainlinkSettings;
    private readonly ILogger<TaskLifecycleWorkflow> _logger;

    /// <summary>
    /// Minimum average score threshold for a milestone to be considered passed
    /// when not all individual strategies report a pass.
    /// </summary>
    private const double PassThreshold = 0.55;

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
        IChainlinkFunctionsClient chainlinkFunctions,
        OnChainReputationService onChainReputation,
        IOptions<ChainlinkSettings> chainlinkSettings,
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
        _chainlinkFunctions = chainlinkFunctions;
        _onChainReputation = onChainReputation;
        _chainlinkSettings = chainlinkSettings.Value;
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
        var startTime = DateTime.UtcNow;

        _logger.LogInformation(
            "Processing submission for milestone {MilestoneId} ({OutputBytes} bytes)",
            milestoneId, agentOutput.Length);

        // 1. Get milestone from DB
        var milestone = await _milestoneRepo.GetByIdAsync(milestoneId, ct)
            ?? throw new InvalidOperationException($"Milestone {milestoneId} not found");

        // 2. Store agent output and update status to Verifying
        milestone.OutputData = Convert.ToBase64String(agentOutput);
        milestone.Status = MilestoneStatus.Verifying;
        await _milestoneRepo.UpdateAsync(milestone, ct);

        // 3. Run verification pipeline
        var pipelineResult = await _verificationPipeline.RunVerificationAsync(milestone, agentOutput, ct);
        var results = pipelineResult.Results;

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

        // 5. Determine overall pass/fail using weighted score from pipeline
        bool allPassed = results.Count > 0 && results.All(r => r.Passed);
        double averageScore = pipelineResult.WeightedScore;
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

            // 6b. Settle escrow (best-effort — LND may be unavailable)
            try
            {
                var escrow = await _escrowRepo.GetByMilestoneIdAsync(milestoneId, ct);
                if (escrow is not null)
                {
                    var decryptedHex = LightningAgent.Engine.Security.PreimageProtector.Unprotect(escrow.PaymentPreimage ?? string.Empty);
                    var preimage = Convert.FromHexString(decryptedHex);
                    await _escrowManager.SettleEscrowAsync(escrow.Id, preimage, ct);
                    _logger.LogInformation("Escrow {EscrowId} settled for milestone {MilestoneId}", escrow.Id, milestoneId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to settle escrow for milestone {MilestoneId} — continuing with payment", milestoneId);
            }

            // 6c. Process payment (best-effort)
            try
            {
                var payment = await _paymentService.ProcessMilestonePaymentAsync(milestoneId, ct);
                _logger.LogInformation("Payment {PaymentId} processed for milestone {MilestoneId}", payment.Id, milestoneId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process payment for milestone {MilestoneId} — milestone still passed", milestoneId);
            }

            // 6d. Update reputation
            if (agentId > 0)
            {
                var elapsedSec = (DateTime.UtcNow - startTime).TotalSeconds;
                await _reputationService.UpdateReputationAsync(
                    agentId,
                    taskCompleted: true,
                    verificationPassed: true,
                    responseTimeSec: elapsedSec,
                    ct);
            }

            // 6e. Post on-chain verification proof via Chainlink Functions
            if (!string.IsNullOrEmpty(_chainlinkSettings.FunctionsRouterAddress))
            {
                try
                {
                    var proofHash = Convert.ToHexString(
                        SHA256.HashData(agentOutput)).ToLowerInvariant();

                    var request = new ChainlinkFunctionRequest
                    {
                        Source = $"return Functions.encodeString('{proofHash}');",
                        Args = new List<string> { proofHash },
                        SubscriptionId = _chainlinkSettings.FunctionsSubscriptionId,
                        DonId = _chainlinkSettings.DonId,
                        CallbackGasLimit = 300_000
                    };

                    var txHash = await _chainlinkFunctions.SendRequestAsync(request, ct);
                    _logger.LogInformation(
                        "On-chain verification proof submitted for milestone {MilestoneId}, proofHash={ProofHash}, txHash={TxHash}",
                        milestoneId, proofHash, txHash);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to post on-chain verification proof for milestone {MilestoneId} — milestone still passed",
                        milestoneId);
                }
            }

            // 6f. Record on-chain reputation attestation via ReputationLedger (best-effort)
            if (!string.IsNullOrEmpty(_chainlinkSettings.ReputationLedgerAddress) && agentId > 0)
            {
                try
                {
                    var proofHash = SHA256.HashData(agentOutput);
                    // Scale the weighted score to an integer (0-1000 basis points)
                    var scoreBasisPoints = (BigInteger)(averageScore * 1000);

                    await _onChainReputation.RecordAttestationAsync(
                        new BigInteger(milestone.TaskId),
                        new BigInteger(milestoneId),
                        new BigInteger(agentId),
                        scoreBasisPoints,
                        passed: true,
                        proofHash,
                        ct);

                    _logger.LogInformation(
                        "On-chain reputation attestation recorded for milestone {MilestoneId}, agentId={AgentId}, score={Score}",
                        milestoneId, agentId, scoreBasisPoints);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to record on-chain reputation attestation for milestone {MilestoneId} — milestone still passed",
                        milestoneId);
                }
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

            // 7b. Cancel escrow (best-effort)
            try
            {
                var escrow = await _escrowRepo.GetByMilestoneIdAsync(milestoneId, ct);
                if (escrow is not null)
                {
                    await _escrowManager.CancelEscrowAsync(escrow.Id, ct);
                    _logger.LogInformation("Escrow {EscrowId} cancelled for failed milestone {MilestoneId}", escrow.Id, milestoneId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cancel escrow for milestone {MilestoneId}", milestoneId);
            }

            // 7c. Update reputation
            if (agentId > 0)
            {
                var elapsedSec = (DateTime.UtcNow - startTime).TotalSeconds;
                await _reputationService.UpdateReputationAsync(
                    agentId,
                    taskCompleted: false,
                    verificationPassed: false,
                    responseTimeSec: elapsedSec,
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
