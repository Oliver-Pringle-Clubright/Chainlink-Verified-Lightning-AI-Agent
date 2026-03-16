using System.Numerics;
using LightningAgentMarketPlace.Chainlink.Contracts;
using LightningAgentMarketPlace.Core.Configuration;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using LightningAgentMarketPlace.Core.Models.Chainlink;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nethereum.ABI;
using Nethereum.ABI.Encoders;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3;

namespace LightningAgentMarketPlace.Chainlink;

public class ChainlinkCcipClient : IChainlinkCcipClient
{
    private readonly ChainlinkSettings _settings;
    private readonly ILogger<ChainlinkCcipClient> _logger;

    /// <summary>
    /// CCIP extraArgs tag for V1 messages: bytes4(keccak256("CCIP EVMExtraArgsV1"))
    /// Followed by ABI-encoded (uint256 gasLimit).
    /// </summary>
    private static readonly byte[] ExtraArgsV1Tag = [0x97, 0xa6, 0x57, 0xc9];
    private const int DefaultGasLimit = 200_000;

    public ChainlinkCcipClient(
        IOptions<ChainlinkSettings> settings,
        ILogger<ChainlinkCcipClient> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<string> SendMessageAsync(
        ulong destinationChainSelector,
        string receiverAddress,
        byte[] payload,
        string feeToken,
        CancellationToken ct = default)
    {
        return await SendCcipMessageAsync(
            destinationChainSelector,
            receiverAddress,
            payload,
            tokenAddress: null,
            tokenAmount: 0,
            feeToken,
            ct);
    }

    public async Task<string> SendTokensAsync(
        ulong destinationChainSelector,
        string receiverAddress,
        string tokenAddress,
        long amountWei,
        byte[]? payload,
        string feeToken,
        CancellationToken ct = default)
    {
        return await SendCcipMessageAsync(
            destinationChainSelector,
            receiverAddress,
            payload ?? Array.Empty<byte>(),
            tokenAddress,
            amountWei,
            feeToken,
            ct);
    }

    public async Task<CcipFeeEstimate> EstimateFeeAsync(
        ulong destinationChainSelector,
        string receiverAddress,
        byte[] payload,
        string feeToken,
        CancellationToken ct = default)
    {
        ValidateRouterAddress();

        _logger.LogInformation(
            "Estimating CCIP fee for destination chain {ChainSelector}",
            destinationChainSelector);

        var web3 = new Web3(_settings.EthereumRpcUrl);
        var contract = web3.Eth.GetContract(CcipRouterAbi.Abi, _settings.CcipRouterAddress);
        var getFeeFunction = contract.GetFunction("getFee");

        var message = BuildEvm2AnyMessage(receiverAddress, payload, null, 0, feeToken);
        var fee = await getFeeFunction.CallAsync<BigInteger>(destinationChainSelector, message);

        _logger.LogInformation("CCIP fee estimate: {Fee} wei for chain {Chain}", fee, destinationChainSelector);

        return new CcipFeeEstimate
        {
            DestinationChainSelector = destinationChainSelector,
            FeeToken = feeToken,
            FeeAmountWei = fee.ToString(),
            EstimatedAt = DateTime.UtcNow
        };
    }

    public async Task<CcipMessage?> GetMessageStatusAsync(string messageId, CancellationToken ct = default)
    {
        // CCIP message status is tracked off-chain via the CCIP Explorer API or on-chain events.
        // This implementation checks the local database via the repository (called from the service layer).
        // The actual on-chain status is polled by the CcipMessagePoller background job.
        _logger.LogDebug("GetMessageStatusAsync called for {MessageId} — status tracked by CcipMessagePoller", messageId);
        return null; // Repository lookup is handled at the service/controller layer
    }

    public async Task<bool> IsChainSupportedAsync(ulong destinationChainSelector, CancellationToken ct = default)
    {
        ValidateRouterAddress();

        var web3 = new Web3(_settings.EthereumRpcUrl);
        var contract = web3.Eth.GetContract(CcipRouterAbi.Abi, _settings.CcipRouterAddress);
        var isChainSupportedFunction = contract.GetFunction("isChainSupported");

        try
        {
            var supported = await isChainSupportedFunction.CallAsync<bool>(destinationChainSelector);
            _logger.LogInformation("Chain {Chain} supported: {Supported}", destinationChainSelector, supported);
            return supported;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check chain support for {Chain}", destinationChainSelector);
            return false;
        }
    }

    private async Task<string> SendCcipMessageAsync(
        ulong destinationChainSelector,
        string receiverAddress,
        byte[] payload,
        string? tokenAddress,
        long tokenAmount,
        string feeToken,
        CancellationToken ct)
    {
        ValidateRouterAddress();

        var account = EthereumAccountProvider.CreateAccount(_settings.PrivateKeyPath)
            ?? throw new InvalidOperationException("Private key is required to send CCIP messages.");

        _logger.LogInformation(
            "Sending CCIP message to chain {ChainSelector}, receiver {Receiver}, payloadSize={Size}",
            destinationChainSelector, receiverAddress, payload.Length);

        var web3 = new Web3(account, _settings.EthereumRpcUrl);
        var contract = web3.Eth.GetContract(CcipRouterAbi.Abi, _settings.CcipRouterAddress);

        // First estimate fee to know how much native token to send
        var getFeeFunction = contract.GetFunction("getFee");
        var message = BuildEvm2AnyMessage(receiverAddress, payload, tokenAddress, tokenAmount, feeToken);
        var fee = await getFeeFunction.CallAsync<BigInteger>(destinationChainSelector, message);

        _logger.LogInformation("CCIP fee: {Fee} wei", fee);

        var ccipSendFunction = contract.GetFunction("ccipSend");

        // If feeToken is address(0), pay in native token; otherwise pay in ERC-20
        var nativeValue = IsNativeToken(feeToken) ? new HexBigInteger(fee) : new HexBigInteger(0);

        var txHash = await ccipSendFunction.SendTransactionAsync(
            account.Address,
            new HexBigInteger(500_000),
            nativeValue,
            destinationChainSelector,
            message);

        _logger.LogInformation("CCIP message sent, txHash={TxHash}", txHash);
        return txHash;
    }

    /// <summary>
    /// Builds the Client.EVM2AnyMessage struct for the CCIP router.
    /// </summary>
    private static object[] BuildEvm2AnyMessage(
        string receiverAddress,
        byte[] data,
        string? tokenAddress,
        long tokenAmount,
        string feeToken)
    {
        // receiver: abi.encode(address) → bytes
        var receiverBytes = new ABIEncode().GetABIEncoded(
            new ABIValue("address", receiverAddress));

        // tokenAmounts: array of (token, amount) tuples
        var tokenAmounts = tokenAddress is not null && tokenAmount > 0
            ? new object[] { new object[] { tokenAddress, new BigInteger(tokenAmount) } }
            : Array.Empty<object>();

        // extraArgs: EVMExtraArgsV1 — tag + abi.encode(gasLimit)
        var gasLimitEncoded = new ABIEncode().GetABIEncoded(
            new ABIValue("uint256", new BigInteger(DefaultGasLimit)));
        var extraArgs = new byte[ExtraArgsV1Tag.Length + gasLimitEncoded.Length];
        ExtraArgsV1Tag.CopyTo(extraArgs, 0);
        gasLimitEncoded.CopyTo(extraArgs, ExtraArgsV1Tag.Length);

        return [receiverBytes, data, tokenAmounts, feeToken, extraArgs];
    }

    private static bool IsNativeToken(string feeToken) =>
        string.IsNullOrEmpty(feeToken) ||
        feeToken == "0x0000000000000000000000000000000000000000";

    private void ValidateRouterAddress()
    {
        if (string.IsNullOrWhiteSpace(_settings.CcipRouterAddress))
            throw new InvalidOperationException(
                "Chainlink:CcipRouterAddress is not configured. " +
                "Set the CCIP router contract address for your network.");
    }
}
