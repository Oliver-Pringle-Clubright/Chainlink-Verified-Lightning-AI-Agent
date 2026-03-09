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

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChainlinkResponsePoller> _logger;

    public ChainlinkResponsePoller(
        IServiceScopeFactory scopeFactory,
        ILogger<ChainlinkResponsePoller> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
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
                        var response = await functionsClient.GetResponseAsync(
                            verification.ChainlinkRequestId!, stoppingToken);

                        if (response is null)
                        {
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
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
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
