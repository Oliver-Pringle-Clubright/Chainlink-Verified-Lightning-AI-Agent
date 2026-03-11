using LightningAgent.Core.Models.Chainlink;

namespace LightningAgent.Core.Interfaces.Services;

public interface IChainlinkCcipClient
{
    /// <summary>
    /// Sends an arbitrary bytes payload to a receiver contract on the destination chain.
    /// Returns the CCIP message ID (bytes32 hex).
    /// </summary>
    Task<string> SendMessageAsync(
        ulong destinationChainSelector,
        string receiverAddress,
        byte[] payload,
        string feeToken,
        CancellationToken ct = default);

    /// <summary>
    /// Sends a cross-chain token transfer (with optional data payload) via CCIP.
    /// Returns the CCIP message ID.
    /// </summary>
    Task<string> SendTokensAsync(
        ulong destinationChainSelector,
        string receiverAddress,
        string tokenAddress,
        long amountWei,
        byte[]? payload,
        string feeToken,
        CancellationToken ct = default);

    /// <summary>
    /// Estimates the CCIP fee for sending a message to the destination chain.
    /// </summary>
    Task<CcipFeeEstimate> EstimateFeeAsync(
        ulong destinationChainSelector,
        string receiverAddress,
        byte[] payload,
        string feeToken,
        CancellationToken ct = default);

    /// <summary>
    /// Checks the delivery status of a previously sent CCIP message.
    /// </summary>
    Task<CcipMessage?> GetMessageStatusAsync(string messageId, CancellationToken ct = default);

    /// <summary>
    /// Returns the set of destination chains supported by the configured router.
    /// </summary>
    Task<bool> IsChainSupportedAsync(ulong destinationChainSelector, CancellationToken ct = default);
}
