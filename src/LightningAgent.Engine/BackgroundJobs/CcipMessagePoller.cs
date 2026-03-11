using LightningAgent.Core.Configuration;
using LightningAgent.Core.Interfaces.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Web3;

namespace LightningAgent.Engine.BackgroundJobs;

/// <summary>
/// Polls for delivery confirmation of outbound CCIP messages by checking
/// the transaction receipt for the CCIPSendRequested event log.
/// Marks messages as Delivered when confirmed, or Failed after timeout.
/// </summary>
public class CcipMessagePoller : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromHours(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CcipMessagePoller> _logger;

    public CcipMessagePoller(
        IServiceScopeFactory scopeFactory,
        ILogger<CcipMessagePoller> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CcipMessagePoller started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var ccipRepo = scope.ServiceProvider.GetRequiredService<ICcipMessageRepository>();
                var settings = scope.ServiceProvider.GetRequiredService<IOptions<ChainlinkSettings>>().Value;

                if (string.IsNullOrWhiteSpace(settings.EthereumRpcUrl) ||
                    string.IsNullOrWhiteSpace(settings.CcipRouterAddress))
                {
                    // CCIP not configured, skip polling
                    await SafeDelay(PollInterval, stoppingToken);
                    continue;
                }

                var pendingMessages = await ccipRepo.GetPendingOutboundAsync(stoppingToken);

                if (pendingMessages.Count > 0)
                {
                    _logger.LogDebug("CcipMessagePoller checking {Count} pending messages", pendingMessages.Count);
                }

                var web3 = new Web3(settings.EthereumRpcUrl);

                foreach (var message in pendingMessages)
                {
                    try
                    {
                        // Check if timed out
                        if (DateTime.UtcNow - message.CreatedAt > MessageTimeout)
                        {
                            _logger.LogWarning(
                                "CCIP message {MessageId} timed out after {Timeout}",
                                message.MessageId, MessageTimeout);
                            message.Status = "Failed";
                            message.ErrorDetails = $"Message delivery timed out after {MessageTimeout.TotalMinutes} minutes";
                            await ccipRepo.UpdateAsync(message, stoppingToken);
                            continue;
                        }

                        if (string.IsNullOrEmpty(message.TxHash))
                            continue;

                        // Check transaction receipt for confirmation
                        var receipt = await web3.Eth.Transactions
                            .GetTransactionReceipt
                            .SendRequestAsync(message.TxHash);

                        if (receipt is null)
                        {
                            _logger.LogDebug("CCIP message {MessageId} tx not yet mined", message.MessageId);
                            continue;
                        }

                        if (receipt.Status.Value == 1)
                        {
                            // Transaction succeeded — extract messageId from logs if available
                            var messageId = ExtractMessageIdFromLogs(receipt);
                            if (!string.IsNullOrEmpty(messageId))
                            {
                                message.MessageId = messageId;
                            }

                            message.Status = "Delivered";
                            message.DeliveredAt = DateTime.UtcNow;
                            _logger.LogInformation(
                                "CCIP message {MessageId} delivered (tx={TxHash})",
                                message.MessageId, message.TxHash);
                        }
                        else
                        {
                            message.Status = "Failed";
                            message.ErrorDetails = "Transaction reverted on-chain";
                            _logger.LogWarning(
                                "CCIP message {MessageId} failed — tx reverted (tx={TxHash})",
                                message.MessageId, message.TxHash);
                        }

                        await ccipRepo.UpdateAsync(message, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "CcipMessagePoller failed to process message {MessageId}",
                            message.MessageId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CcipMessagePoller encountered an error during poll cycle");
            }

            await SafeDelay(PollInterval, stoppingToken);
        }

        _logger.LogInformation("CcipMessagePoller stopped");
    }

    /// <summary>
    /// Attempts to extract the CCIP messageId (bytes32) from the CCIPSendRequested event log.
    /// </summary>
    private static string? ExtractMessageIdFromLogs(Nethereum.RPC.Eth.DTOs.TransactionReceipt receipt)
    {
        // CCIPSendRequested event topic0: keccak256("CCIPSendRequested(bytes32,uint64,bytes,uint256[],uint256)")
        // The messageId is indexed (topic1)
        foreach (var log in receipt.Logs)
        {
            var jLog = log as Newtonsoft.Json.Linq.JObject;
            if (jLog is null) continue;

            var topics = jLog["topics"] as Newtonsoft.Json.Linq.JArray;
            if (topics is null || topics.Count < 2) continue;

            // topic[1] is the indexed messageId (bytes32)
            var messageIdHex = topics[1]?.ToString();
            if (!string.IsNullOrEmpty(messageIdHex))
                return messageIdHex;
        }

        return null;
    }

    private static async Task SafeDelay(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
