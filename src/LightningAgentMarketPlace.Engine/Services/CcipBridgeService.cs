using System.Text;
using System.Text.Json;
using LightningAgentMarketPlace.Chainlink;
using LightningAgentMarketPlace.Core.Configuration;
using LightningAgentMarketPlace.Core.Interfaces.Data;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using LightningAgentMarketPlace.Core.Models.Chainlink;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightningAgentMarketPlace.Engine.Services;

/// <summary>
/// High-level service for cross-chain agent communication via Chainlink CCIP.
/// Supports sending task assignments, verification proofs, and payment settlements across chains.
/// </summary>
public class CcipBridgeService
{
    private readonly IChainlinkCcipClient _ccipClient;
    private readonly ICcipMessageRepository _ccipRepo;
    private readonly ChainlinkSettings _settings;
    private readonly ILogger<CcipBridgeService> _logger;

    public CcipBridgeService(
        IChainlinkCcipClient ccipClient,
        ICcipMessageRepository ccipRepo,
        IOptions<ChainlinkSettings> settings,
        ILogger<CcipBridgeService> logger)
    {
        _ccipClient = ccipClient;
        _ccipRepo = ccipRepo;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Sends a task assignment to a worker agent on another chain.
    /// The payload contains the serialized task spec for the remote agent.
    /// </summary>
    public async Task<CcipMessage> SendTaskAssignmentAsync(
        ulong destinationChain,
        string receiverContract,
        int taskId,
        int agentId,
        object taskSpec,
        CancellationToken ct = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "TaskAssignment",
            taskId,
            agentId,
            spec = taskSpec,
            timestamp = DateTime.UtcNow
        });

        var txHash = await _ccipClient.SendMessageAsync(
            destinationChain, receiverContract, payload, FeeTokenAddress(), ct);

        var message = new CcipMessage
        {
            MessageId = txHash, // Will be updated to actual CCIP messageId by the poller
            SourceChainSelector = _settings.CcipSourceChainSelector,
            DestinationChainSelector = destinationChain,
            SenderAddress = GetSenderAddress(),
            ReceiverAddress = receiverContract,
            Payload = Convert.ToBase64String(payload),
            FeeToken = FeeTokenAddress(),
            Direction = "Outbound",
            Status = "Sent",
            TxHash = txHash,
            TaskId = taskId,
            AgentId = agentId,
            CreatedAt = DateTime.UtcNow
        };

        message.Id = await _ccipRepo.CreateAsync(message, ct);

        _logger.LogInformation(
            "Cross-chain task assignment sent: task={TaskId}, agent={AgentId}, chain={Chain}, tx={TxHash}",
            taskId, agentId, destinationChain, txHash);

        return message;
    }

    /// <summary>
    /// Sends a verification proof to the destination chain for on-chain attestation.
    /// </summary>
    public async Task<CcipMessage> SendVerificationProofAsync(
        ulong destinationChain,
        string receiverContract,
        int taskId,
        string proofHash,
        double score,
        bool passed,
        CancellationToken ct = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "VerificationProof",
            taskId,
            proofHash,
            score,
            passed,
            timestamp = DateTime.UtcNow
        });

        var txHash = await _ccipClient.SendMessageAsync(
            destinationChain, receiverContract, payload, FeeTokenAddress(), ct);

        var message = new CcipMessage
        {
            MessageId = txHash,
            SourceChainSelector = _settings.CcipSourceChainSelector,
            DestinationChainSelector = destinationChain,
            SenderAddress = GetSenderAddress(),
            ReceiverAddress = receiverContract,
            Payload = Convert.ToBase64String(payload),
            FeeToken = FeeTokenAddress(),
            Direction = "Outbound",
            Status = "Sent",
            TxHash = txHash,
            TaskId = taskId,
            CreatedAt = DateTime.UtcNow
        };

        message.Id = await _ccipRepo.CreateAsync(message, ct);

        _logger.LogInformation(
            "Cross-chain verification proof sent: task={TaskId}, chain={Chain}, tx={TxHash}",
            taskId, destinationChain, txHash);

        return message;
    }

    /// <summary>
    /// Sends a cross-chain token transfer for payment settlement.
    /// </summary>
    public async Task<CcipMessage> SendPaymentAsync(
        ulong destinationChain,
        string receiverAddress,
        string tokenAddress,
        long amountWei,
        int? taskId,
        int? agentId,
        CancellationToken ct = default)
    {
        var paymentData = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "PaymentSettlement",
            taskId,
            agentId,
            timestamp = DateTime.UtcNow
        });

        var txHash = await _ccipClient.SendTokensAsync(
            destinationChain, receiverAddress, tokenAddress, amountWei,
            paymentData, FeeTokenAddress(), ct);

        var message = new CcipMessage
        {
            MessageId = txHash,
            SourceChainSelector = _settings.CcipSourceChainSelector,
            DestinationChainSelector = destinationChain,
            SenderAddress = GetSenderAddress(),
            ReceiverAddress = receiverAddress,
            Payload = Convert.ToBase64String(paymentData),
            TokenAddress = tokenAddress,
            TokenAmountWei = amountWei,
            FeeToken = FeeTokenAddress(),
            Direction = "Outbound",
            Status = "Sent",
            TxHash = txHash,
            TaskId = taskId,
            AgentId = agentId,
            CreatedAt = DateTime.UtcNow
        };

        message.Id = await _ccipRepo.CreateAsync(message, ct);

        _logger.LogInformation(
            "Cross-chain payment sent: amount={Amount}, token={Token}, chain={Chain}, tx={TxHash}",
            amountWei, tokenAddress, destinationChain, txHash);

        return message;
    }

    /// <summary>
    /// Estimates the CCIP fee for a cross-chain message.
    /// </summary>
    public async Task<CcipFeeEstimate> EstimateFeeAsync(
        ulong destinationChain,
        string receiverAddress,
        byte[]? payload,
        CancellationToken ct = default)
    {
        return await _ccipClient.EstimateFeeAsync(
            destinationChain, receiverAddress,
            payload ?? Array.Empty<byte>(),
            FeeTokenAddress(), ct);
    }

    /// <summary>
    /// Checks if a destination chain is supported by the CCIP router.
    /// </summary>
    public async Task<bool> IsChainSupportedAsync(ulong chainSelector, CancellationToken ct = default)
    {
        return await _ccipClient.IsChainSupportedAsync(chainSelector, ct);
    }

    /// <summary>
    /// Returns the well-known chain selectors for Chainlink CCIP testnet/mainnet lanes.
    /// </summary>
    public static IReadOnlyList<CcipSupportedChain> GetKnownChains() =>
    [
        new() { ChainSelector = 16015286601757825753, Name = "Ethereum Sepolia", NetworkType = "Testnet",
                RouterAddress = "0x0BF3dE8c5D3e8A2B34D2BEeB17ABfCeBaf363A59" },
        new() { ChainSelector = 14767482510784806043, Name = "Avalanche Fuji", NetworkType = "Testnet",
                RouterAddress = "0xF694E193200268f9a4868e4Aa017A0118C9a8177" },
        new() { ChainSelector = 2664363617261496610, Name = "Optimism Sepolia", NetworkType = "Testnet",
                RouterAddress = "0x114A20A10b43D4115e5aeef7345a1A71d2a60C57" },
        new() { ChainSelector = 3478487238524512106, Name = "Arbitrum Sepolia", NetworkType = "Testnet",
                RouterAddress = "0x2a9C5afB0d0e4BAb2BCdaE109EC4b0c4Be15a165" },
        new() { ChainSelector = 10344971235874465080, Name = "Base Sepolia", NetworkType = "Testnet",
                RouterAddress = "0xD3b06cEbF099CE7DA4AcCf578aaebFDBd6e88a93" },
        new() { ChainSelector = 9284632837123596123, Name = "Polygon Amoy", NetworkType = "Testnet",
                RouterAddress = "0x9C32fCB86BF0f4a1A8921a9Fe46de3198bb884B2" },
        new() { ChainSelector = 13264668187771770619, Name = "BNB Testnet", NetworkType = "Testnet",
                RouterAddress = "0xE1053aE1857476f36A3C62580FF9b016E8EE8F6f" },
        // Mainnet lanes
        new() { ChainSelector = 5009297550715157269, Name = "Ethereum Mainnet", NetworkType = "Mainnet",
                RouterAddress = "0x80226fc0Ee2b096224EeAc085Bb9a8cba1146f7D" },
        new() { ChainSelector = 4949039107694359620, Name = "Arbitrum One", NetworkType = "Mainnet",
                RouterAddress = "0x141fa059441E0ca23ce184B6A78bafD2A517DdE8" },
        new() { ChainSelector = 3734403246176062136, Name = "Optimism", NetworkType = "Mainnet",
                RouterAddress = "0x3206695CaE29952f4b0c22a169725a865bc43287" },
        new() { ChainSelector = 15971525489660198786, Name = "Base", NetworkType = "Mainnet",
                RouterAddress = "0x881e3A65B4d4a04310c13bF528eE38c8c0344527" },
        new() { ChainSelector = 11344663589394136015, Name = "BNB Smart Chain", NetworkType = "Mainnet",
                RouterAddress = "0x34B03Cb9086d7D758AC55af71584F81A598759FE" },
        new() { ChainSelector = 4051577828743386545, Name = "Polygon", NetworkType = "Mainnet",
                RouterAddress = "0x849c5ED5a80F5B408Dd4969b78c2C8fdf0565Bfe" },
    ];

    private string FeeTokenAddress() =>
        // address(0) means pay in native token (ETH)
        "0x0000000000000000000000000000000000000000";

    private string GetSenderAddress()
    {
        var account = EthereumAccountProvider.CreateAccount(_settings.PrivateKeyPath);
        return account?.Address ?? "0x0000000000000000000000000000000000000000";
    }
}
