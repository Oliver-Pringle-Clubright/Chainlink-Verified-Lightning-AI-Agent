using System.Collections.Concurrent;
using System.Numerics;
using LightningAgent.Chainlink.Contracts;
using LightningAgent.Core.Configuration;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models.Chainlink;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Web3;

namespace LightningAgent.Chainlink;

public class ChainlinkFunctionsClient : IChainlinkFunctionsClient
{
    private readonly ChainlinkSettings _settings;
    private readonly ILogger<ChainlinkFunctionsClient> _logger;

    /// <summary>
    /// Tracks pending request IDs with their submission timestamps for timeout detection.
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTime> _pendingRequests = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Default timeout for a Chainlink Functions response (5 minutes).
    /// </summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum number of poll attempts before giving up on a single GetResponseAsync call.
    /// This is a per-call safeguard; the poller also enforces its own retry limit.
    /// </summary>
    private const int MaxPollAttempts = 1; // Each call is a single check; retry logic lives in the poller.

    public ChainlinkFunctionsClient(
        IOptions<ChainlinkSettings> settings,
        ILogger<ChainlinkFunctionsClient> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<string> SendRequestAsync(ChainlinkFunctionRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Sending Chainlink Functions request (subId={SubId}, donId={DonId})",
            request.SubscriptionId, request.DonId);

        var account = EthereumAccountProvider.CreateAccount(_settings.PrivateKeyPath)
            ?? throw new InvalidOperationException("Private key is required to send Functions requests.");

        var web3 = new Web3(account, _settings.EthereumRpcUrl);
        var contract = web3.Eth.GetContract(FunctionsConsumerAbi.Abi, _settings.FunctionsRouterAddress);
        var sendRequestFunction = contract.GetFunction("sendRequest");

        var donIdBytes = new byte[32];
        var donIdUtf8 = System.Text.Encoding.UTF8.GetBytes(request.DonId);
        Array.Copy(donIdUtf8, donIdBytes, Math.Min(donIdUtf8.Length, 32));

        var txHash = await sendRequestFunction.SendTransactionAsync(
            account.Address,
            new Nethereum.Hex.HexTypes.HexBigInteger(500_000),
            null,
            request.Source,
            Array.Empty<byte>(),
            (byte)0,
            (ulong)0,
            request.Args?.ToArray() ?? Array.Empty<string>(),
            Array.Empty<byte[]>(),
            (ulong)request.SubscriptionId,
            (uint)request.CallbackGasLimit,
            donIdBytes
        );

        _logger.LogInformation("Functions request sent, txHash={TxHash}", txHash);

        // Track this request as pending
        _pendingRequests.TryAdd(txHash, DateTime.UtcNow);

        return txHash;
    }

    public async Task<ChainlinkFunctionResponse?> GetResponseAsync(string requestId, CancellationToken ct = default)
    {
        _logger.LogInformation("Checking Functions response for requestId={RequestId}", requestId);

        // Check if this request has timed out
        if (_pendingRequests.TryGetValue(requestId, out var submittedAt))
        {
            if (DateTime.UtcNow - submittedAt > DefaultTimeout)
            {
                // Remove from pending and return a timeout error response
                _pendingRequests.TryRemove(requestId, out _);
                _logger.LogWarning("Chainlink Functions request {RequestId} timed out after {Timeout}", requestId, DefaultTimeout);
                return new ChainlinkFunctionResponse
                {
                    RequestId = requestId,
                    Response = Array.Empty<byte>(),
                    Error = System.Text.Encoding.UTF8.GetBytes("Request timed out waiting for Chainlink Functions response"),
                    TxHash = requestId
                };
            }
        }

        var web3 = new Web3(_settings.EthereumRpcUrl);
        var contract = web3.Eth.GetContract(FunctionsConsumerAbi.Abi, _settings.FunctionsRouterAddress);

        var lastRequestIdFunction = contract.GetFunction("s_lastRequestId");
        var lastResponseFunction = contract.GetFunction("s_lastResponse");
        var lastErrorFunction = contract.GetFunction("s_lastError");

        var lastRequestIdBytes = await lastRequestIdFunction.CallAsync<byte[]>();
        var lastRequestIdHex = lastRequestIdBytes.ToHex(prefix: true);

        if (!string.Equals(lastRequestIdHex, requestId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Response not ready yet for requestId={RequestId} (last on-chain={LastRequestId})",
                requestId, lastRequestIdHex);
            return null; // Not ready yet -- the poller should retry
        }

        var responseBytes = await lastResponseFunction.CallAsync<byte[]>();
        var errorBytes = await lastErrorFunction.CallAsync<byte[]>();

        // Request fulfilled; remove from pending set
        _pendingRequests.TryRemove(requestId, out _);

        return new ChainlinkFunctionResponse
        {
            RequestId = requestId,
            Response = responseBytes ?? Array.Empty<byte>(),
            Error = errorBytes ?? Array.Empty<byte>(),
            TxHash = requestId
        };
    }

    /// <inheritdoc />
    public bool IsRequestPending(string requestId)
    {
        if (!_pendingRequests.TryGetValue(requestId, out var submittedAt))
            return false;

        // If it has exceeded the timeout, it's no longer pending -- it's timed out
        if (DateTime.UtcNow - submittedAt > DefaultTimeout)
        {
            _pendingRequests.TryRemove(requestId, out _);
            return false;
        }

        return true;
    }
}
