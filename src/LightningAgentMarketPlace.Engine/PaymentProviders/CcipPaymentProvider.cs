using LightningAgentMarketPlace.Core.Configuration;
using LightningAgentMarketPlace.Core.Enums;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using LightningAgentMarketPlace.Engine.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightningAgentMarketPlace.Engine.PaymentProviders;

/// <summary>
/// Sends cross-chain token transfers via Chainlink CCIP.
/// Client pays on one chain, agent receives on another.
/// </summary>
public class CcipPaymentProvider : IPaymentProvider
{
    private readonly CcipBridgeService _bridge;
    private readonly ChainlinkSettings _settings;
    private readonly ILogger<CcipPaymentProvider> _logger;

    public PaymentMethod Method => PaymentMethod.CcipBridge;
    public bool IsAvailable => !string.IsNullOrEmpty(_settings.CcipRouterAddress);

    public CcipPaymentProvider(
        CcipBridgeService bridge,
        IOptions<ChainlinkSettings> settings,
        ILogger<CcipPaymentProvider> logger)
    {
        _bridge = bridge;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PaymentResult> SendPaymentAsync(PaymentRequest request, CancellationToken ct = default)
    {
        if (request.ChainId is null)
            return Fail("Destination chain ID is required for CCIP transfers");

        // Find the CCIP chain selector for the destination chain
        var knownChains = CcipBridgeService.GetKnownChains();
        ulong destSelector = 0;
        foreach (var chain in knownChains)
        {
            // Match by name containing the chain identifier
            var chainName = ChainlinkAddressRegistry.GetChainName(request.ChainId.Value);
            if (chain.Name.Contains(chainName, StringComparison.OrdinalIgnoreCase))
            {
                destSelector = chain.ChainSelector;
                break;
            }
        }

        if (destSelector == 0)
            return Fail($"Chain {request.ChainId} is not supported for CCIP transfers");

        try
        {
            var tokenAddress = request.TokenAddress
                ?? TokenAddressRegistry.GetLinkAddress(1)
                ?? "";

            var message = await _bridge.SendPaymentAsync(
                destSelector, request.ReceiverAddress, tokenAddress,
                request.AmountSats, request.TaskId, request.AgentId, ct);

            _logger.LogInformation(
                "CCIP transfer sent to chain {ChainId}: {Amount} sats, messageId={MessageId}",
                request.ChainId, request.AmountSats, message.MessageId);

            return new PaymentResult
            {
                Success = true,
                TransactionHash = message.MessageId,
                PaymentType = PaymentType.CcipTransfer,
                PaymentMethod = PaymentMethod.CcipBridge,
                ChainId = request.ChainId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CCIP transfer failed to chain {ChainId}", request.ChainId);
            return Fail(ex.Message);
        }
    }

    private static PaymentResult Fail(string error) => new()
    {
        Success = false,
        Error = error,
        PaymentType = PaymentType.CcipTransfer,
        PaymentMethod = PaymentMethod.CcipBridge
    };
}
