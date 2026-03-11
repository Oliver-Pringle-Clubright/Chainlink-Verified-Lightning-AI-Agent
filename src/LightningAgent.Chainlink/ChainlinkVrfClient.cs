using System.Collections.Concurrent;
using System.Numerics;
using LightningAgent.Chainlink.Contracts;
using LightningAgent.Core.Configuration;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models.Chainlink;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3;

namespace LightningAgent.Chainlink;

public class ChainlinkVrfClient : IChainlinkVrfClient
{
    private readonly ChainlinkSettings _settings;
    private readonly ILogger<ChainlinkVrfClient> _logger;

    /// <summary>
    /// Tracks pending VRF request IDs with submission timestamps for timeout detection.
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTime> _pendingRequests = new(StringComparer.OrdinalIgnoreCase);

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    public ChainlinkVrfClient(
        IOptions<ChainlinkSettings> settings,
        ILogger<ChainlinkVrfClient> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_settings.VrfCoordinatorAddress) &&
        !string.IsNullOrWhiteSpace(_settings.VrfConsumerAddress) &&
        !string.IsNullOrWhiteSpace(_settings.VrfKeyHash);

    public async Task<ChainlinkVrfRequest> RequestRandomnessAsync(int numWords, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                "VRF is not fully configured. Set Chainlink:VrfCoordinatorAddress, " +
                "Chainlink:VrfConsumerAddress, and Chainlink:VrfKeyHash.");

        _logger.LogInformation("Requesting {NumWords} random words from VRF coordinator", numWords);

        var account = EthereumAccountProvider.CreateAccount(_settings.PrivateKeyPath)
            ?? throw new InvalidOperationException("Private key is required to request VRF randomness.");

        var web3 = new Web3(account, _settings.EthereumRpcUrl);
        var contract = web3.Eth.GetContract(VrfCoordinatorAbi.Abi, _settings.VrfCoordinatorAddress);
        var requestFunction = contract.GetFunction("requestRandomWords");

        var keyHash = _settings.VrfKeyHash.HexToByteArray();
        var subscriptionId = BigInteger.Parse(_settings.SubscriptionId);
        ushort requestConfirmations = 3;
        uint callbackGasLimit = 100_000;

        var txHash = await requestFunction.SendTransactionAsync(
            account.Address,
            new HexBigInteger(300_000),
            null,
            keyHash,
            subscriptionId,
            requestConfirmations,
            callbackGasLimit,
            (uint)numWords
        );

        _logger.LogInformation("VRF request sent, txHash={TxHash}", txHash);

        // Track as pending
        _pendingRequests.TryAdd(txHash, DateTime.UtcNow);

        return new ChainlinkVrfRequest
        {
            RequestId = txHash,
            NumWords = numWords
        };
    }

    public async Task<ChainlinkVrfRequest?> GetFulfillmentAsync(string requestId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.VrfConsumerAddress))
        {
            _logger.LogWarning("VrfConsumerAddress not configured, cannot check fulfillment");
            return null;
        }

        // Check timeout
        if (_pendingRequests.TryGetValue(requestId, out var submittedAt) &&
            DateTime.UtcNow - submittedAt > DefaultTimeout)
        {
            _pendingRequests.TryRemove(requestId, out _);
            _logger.LogWarning("VRF request {RequestId} timed out after {Timeout}", requestId, DefaultTimeout);
            return null;
        }

        var web3 = new Web3(_settings.EthereumRpcUrl);
        var contract = web3.Eth.GetContract(VrfConsumerAbi.Abi, _settings.VrfConsumerAddress);

        try
        {
            // Try getRequestStatus(requestId) on the consumer contract
            var getStatusFunction = contract.GetFunction("getRequestStatus");

            // The requestId from the tx needs to be the actual VRF requestId.
            // If we sent via the consumer contract, it stores the mapping.
            // Try to read the last request ID from the consumer for correlation.
            var lastRequestIdFunction = contract.GetFunction("s_lastRequestId");
            var lastRequestId = await lastRequestIdFunction.CallAsync<BigInteger>();

            // Query fulfillment for the last known request
            var result = await getStatusFunction.CallDeserializingToObjectAsync<VrfRequestStatusOutput>(lastRequestId);

            if (!result.Fulfilled)
            {
                _logger.LogDebug("VRF request not yet fulfilled (requestId={RequestId})", requestId);
                return null;
            }

            // Convert BigInteger[] to string list
            var randomWords = result.RandomWords
                .Select(w => w.ToString())
                .ToList();

            _pendingRequests.TryRemove(requestId, out _);

            _logger.LogInformation(
                "VRF request fulfilled: {Count} random words received (requestId={RequestId})",
                randomWords.Count, requestId);

            return new ChainlinkVrfRequest
            {
                RequestId = requestId,
                NumWords = randomWords.Count,
                Randomness = randomWords
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to check VRF fulfillment for {RequestId}", requestId);
            return null;
        }
    }

    [Nethereum.ABI.FunctionEncoding.Attributes.FunctionOutput]
    private class VrfRequestStatusOutput : Nethereum.ABI.FunctionEncoding.Attributes.IFunctionOutputDTO
    {
        [Nethereum.ABI.FunctionEncoding.Attributes.Parameter("bool", "fulfilled", 1)]
        public bool Fulfilled { get; set; }

        [Nethereum.ABI.FunctionEncoding.Attributes.Parameter("uint256[]", "randomWords", 2)]
        public List<BigInteger> RandomWords { get; set; } = new();
    }
}
