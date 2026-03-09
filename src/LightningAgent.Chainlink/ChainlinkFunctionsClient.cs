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
        return txHash;
    }

    public async Task<ChainlinkFunctionResponse?> GetResponseAsync(string requestId, CancellationToken ct = default)
    {
        _logger.LogInformation("Checking Functions response for requestId={RequestId}", requestId);

        var web3 = new Web3(_settings.EthereumRpcUrl);
        var contract = web3.Eth.GetContract(FunctionsConsumerAbi.Abi, _settings.FunctionsRouterAddress);

        var lastRequestIdFunction = contract.GetFunction("s_lastRequestId");
        var lastResponseFunction = contract.GetFunction("s_lastResponse");
        var lastErrorFunction = contract.GetFunction("s_lastError");

        var lastRequestIdBytes = await lastRequestIdFunction.CallAsync<byte[]>();
        var lastRequestIdHex = lastRequestIdBytes.ToHex(prefix: true);

        if (!string.Equals(lastRequestIdHex, requestId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Response not ready yet for requestId={RequestId}", requestId);
            return null;
        }

        var responseBytes = await lastResponseFunction.CallAsync<byte[]>();
        var errorBytes = await lastErrorFunction.CallAsync<byte[]>();

        return new ChainlinkFunctionResponse
        {
            RequestId = requestId,
            Response = responseBytes ?? Array.Empty<byte>(),
            Error = errorBytes ?? Array.Empty<byte>(),
            TxHash = requestId
        };
    }
}
