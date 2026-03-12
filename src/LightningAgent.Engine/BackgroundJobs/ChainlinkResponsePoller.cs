using System.Collections.Concurrent;
using System.Text;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LightningAgent.Engine.BackgroundJobs;

public class ChainlinkResponsePoller : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum number of poll attempts per verification before marking it as timed out.
    /// 60 retries * 30 seconds = 30 minutes.
    /// </summary>
    private const int MaxRetriesPerVerification = 60;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChainlinkResponsePoller> _logger;
    private readonly IServiceHealthTracker _healthTracker;

    /// <summary>
    /// Tracks the number of poll attempts per verification ID.
    /// </summary>
    private readonly ConcurrentDictionary<int, int> _retryCounters = new();

    public ChainlinkResponsePoller(
        IServiceScopeFactory scopeFactory,
        ILogger<ChainlinkResponsePoller> logger,
        IServiceHealthTracker healthTracker)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _healthTracker = healthTracker;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ChainlinkResponsePoller started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var verificationRepo = scope.ServiceProvider.GetRequiredService<IVerificationRepository>();
                var functionsClient = scope.ServiceProvider.GetRequiredService<IChainlinkFunctionsClient>();

                var pendingVerifications = await verificationRepo.GetPendingChainlinkAsync(stoppingToken);

                if (pendingVerifications.Count > 0)
                {
                    _logger.LogDebug(
                        "ChainlinkResponsePoller checking {Count} pending verifications",
                        pendingVerifications.Count);
                }

                foreach (var verification in pendingVerifications)
                {
                    try
                    {
                        // Increment and check retry counter
                        var retryCount = _retryCounters.AddOrUpdate(
                            verification.Id,
                            1,
                            (_, existing) => existing + 1);

                        if (retryCount > MaxRetriesPerVerification)
                        {
                            _logger.LogWarning(
                                "Verification {Id} (requestId={RequestId}) exceeded max retries ({MaxRetries}). " +
                                "Falling back to local AI judge verification then cancelling escrow.",
                                verification.Id, verification.ChainlinkRequestId, MaxRetriesPerVerification);

                            // Attempt local AI judge fallback before marking as failed
                            try
                            {
                                var milestoneRepo = scope.ServiceProvider.GetRequiredService<IMilestoneRepository>();
                                var aiJudge = scope.ServiceProvider.GetRequiredService<LightningAgent.AI.Judge.AiJudgeAgent>();

                                var milestone = await milestoneRepo.GetByIdAsync(verification.MilestoneId, stoppingToken);
                                if (milestone?.OutputData is not null)
                                {
                                    var output = System.Text.Encoding.UTF8.GetString(
                                        Convert.FromBase64String(milestone.OutputData));
                                    var judgeResult = await aiJudge.JudgeOutputAsync(
                                        milestone.Description ?? milestone.Title,
                                        milestone.VerificationCriteria ?? milestone.Description ?? milestone.Title,
                                        output, stoppingToken);

                                    verification.Score = judgeResult.Score;
                                    verification.Passed = judgeResult.Passed;
                                    verification.Details = $"Chainlink timeout — AI judge fallback: {judgeResult.Reasoning}";

                                    _logger.LogInformation(
                                        "Verification {Id} resolved via AI judge fallback: passed={Passed}, score={Score:F2}",
                                        verification.Id, verification.Passed, verification.Score);
                                }
                                else
                                {
                                    verification.Score = 0.0;
                                    verification.Passed = false;
                                    verification.Details = "Chainlink Functions timed out, no output for AI judge fallback";
                                }
                            }
                            catch (Exception fallbackEx)
                            {
                                _logger.LogWarning(fallbackEx,
                                    "AI judge fallback also failed for verification {Id}", verification.Id);
                                verification.Score = 0.0;
                                verification.Passed = false;
                                verification.Details = "Chainlink Functions timed out; AI judge fallback failed";
                            }

                            verification.CompletedAt = DateTime.UtcNow;
                            await verificationRepo.UpdateAsync(verification, stoppingToken);

                            // Auto-cancel the stuck escrow if verification failed
                            if (!verification.Passed)
                            {
                                try
                                {
                                    var escrowRepo = scope.ServiceProvider.GetRequiredService<IEscrowRepository>();
                                    var escrowManager = scope.ServiceProvider.GetRequiredService<IEscrowManager>();
                                    var escrow = await escrowRepo.GetByMilestoneIdAsync(verification.MilestoneId, stoppingToken);
                                    if (escrow is not null && escrow.Status == Core.Enums.EscrowStatus.Held)
                                    {
                                        await escrowManager.CancelEscrowAsync(escrow.Id, stoppingToken);
                                        _logger.LogInformation(
                                            "Auto-cancelled stuck escrow {EscrowId} for timed-out verification {VerificationId}",
                                            escrow.Id, verification.Id);
                                    }
                                }
                                catch (Exception escrowEx)
                                {
                                    _logger.LogWarning(escrowEx,
                                        "Failed to auto-cancel escrow for timed-out verification {Id}", verification.Id);
                                }
                            }

                            _retryCounters.TryRemove(verification.Id, out _);
                            continue;
                        }

                        var response = await functionsClient.GetResponseAsync(
                            verification.ChainlinkRequestId!, stoppingToken);

                        if (response is null)
                        {
                            _logger.LogDebug(
                                "Verification {Id} (requestId={RequestId}): not ready yet (attempt {Attempt}/{Max})",
                                verification.Id, verification.ChainlinkRequestId, retryCount, MaxRetriesPerVerification);
                            continue;
                        }

                        // Parse the response bytes as a verification score
                        var responseText = response.Response.Length > 0
                            ? Encoding.UTF8.GetString(response.Response)
                            : string.Empty;

                        var errorText = response.Error.Length > 0
                            ? Encoding.UTF8.GetString(response.Error)
                            : null;

                        if (errorText is not null)
                        {
                            verification.Score = 0.0;
                            verification.Passed = false;
                            verification.Details = $"Chainlink Functions error: {errorText}";
                        }
                        else
                        {
                            // Attempt to parse score from response; default to pass if non-empty
                            double score = double.TryParse(responseText, out var parsed) ? parsed : 1.0;
                            verification.Score = score;
                            verification.Passed = score >= 0.5;
                            verification.Details = responseText;
                        }

                        verification.ChainlinkTxHash = response.TxHash;
                        verification.CompletedAt = DateTime.UtcNow;

                        await verificationRepo.UpdateAsync(verification, stoppingToken);
                        _retryCounters.TryRemove(verification.Id, out _);

                        _logger.LogInformation(
                            "Verification {Id} completed via Chainlink (requestId={RequestId}, passed={Passed}, score={Score:F2})",
                            verification.Id, verification.ChainlinkRequestId, verification.Passed, verification.Score);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "ChainlinkResponsePoller failed to process verification {Id} (requestId={RequestId})",
                            verification.Id, verification.ChainlinkRequestId);
                    }
                }

                _healthTracker.RecordSuccess("ChainlinkResponsePoller");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _healthTracker.RecordFailure("ChainlinkResponsePoller", ex.Message);
                _logger.LogError(ex, "ChainlinkResponsePoller encountered an error during poll cycle");
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

        _logger.LogInformation("ChainlinkResponsePoller stopped");
    }
}
