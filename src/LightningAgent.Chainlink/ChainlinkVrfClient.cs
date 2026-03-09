using System.Numerics;
using LightningAgent.Chainlink.Contracts;
using LightningAgent.Core.Configuration;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models.Chainlink;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3;

namespace LightningAgent.Chainlink;

public class ChainlinkVrfClient : IChainlinkVrfClient
{
    private readonly ChainlinkSettings _settings;
    private readonly ILogger<ChainlinkVrfClient> _logger;

    public ChainlinkVrfClient(
        IOptions<ChainlinkSettings> settings,
        ILogger<ChainlinkVrfClient> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ChainlinkVrfRequest> RequestRandomnessAsync(int numWords, CancellationToken ct = default)
    {
        _logger.LogInformation("Requesting {NumWords} random words from VRF", numWords);

        var account = EthereumAccountProvider.CreateAccount(_settings.PrivateKeyPath)
            ?? throw new InvalidOperationException("Private key is required to request VRF randomness.");

        var web3 = new Web3(account, _settings.EthereumRpcUrl);
        var contract = web3.Eth.GetContract(VrfCoordinatorAbi.Abi, _settings.VrfCoordinatorAddress);
        var requestFunction = contract.GetFunction("requestRandomWords");

        // Default key hash (can be made configurable)
        var keyHash = new byte[32];
        var subscriptionId = (ulong)_settings.SubscriptionId;
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

        return new ChainlinkVrfRequest
        {
            RequestId = txHash,
            NumWords = numWords
        };
    }
}
