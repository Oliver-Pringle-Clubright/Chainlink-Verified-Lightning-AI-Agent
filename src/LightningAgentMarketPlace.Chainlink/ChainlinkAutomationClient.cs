using System.Numerics;
using LightningAgentMarketPlace.Chainlink.Contracts;
using LightningAgentMarketPlace.Core.Configuration;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3;

namespace LightningAgentMarketPlace.Chainlink;

public class ChainlinkAutomationClient : IChainlinkAutomationClient
{
    private readonly ChainlinkSettings _settings;
    private readonly ILogger<ChainlinkAutomationClient> _logger;

    public ChainlinkAutomationClient(
        IOptions<ChainlinkSettings> settings,
        ILogger<ChainlinkAutomationClient> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<string> RegisterUpkeepAsync(string target, byte[] checkData, int gasLimit, CancellationToken ct = default)
    {
        _logger.LogInformation("Registering upkeep for target {Target} with gasLimit {GasLimit}", target, gasLimit);

        var account = EthereumAccountProvider.CreateAccount(_settings.PrivateKeyPath)
            ?? throw new InvalidOperationException("Private key is required to register upkeep.");

        var web3 = new Web3(account, _settings.EthereumRpcUrl);
        var contract = web3.Eth.GetContract(AutomationRegistryAbi.Abi, _settings.AutomationRegistryAddress);
        var registerFunction = contract.GetFunction("registerUpkeep");

        var txHash = await registerFunction.SendTransactionAsync(
            account.Address,
            new HexBigInteger(500_000),
            null,
            target,
            (uint)gasLimit,
            account.Address,
            checkData
        );

        _logger.LogInformation("Upkeep registration tx sent: {TxHash}", txHash);
        return txHash;
    }

    public async Task<bool> CheckUpkeepAsync(string upkeepId, CancellationToken ct = default)
    {
        _logger.LogInformation("Checking upkeep {UpkeepId}", upkeepId);

        var web3 = new Web3(_settings.EthereumRpcUrl);
        var contract = web3.Eth.GetContract(AutomationRegistryAbi.Abi, _settings.AutomationRegistryAddress);
        var getUpkeepFunction = contract.GetFunction("getUpkeep");

        try
        {
            var upkeepIdBigInt = BigInteger.Parse(upkeepId);
            var result = await getUpkeepFunction.CallDeserializingToObjectAsync<GetUpkeepOutput>(upkeepIdBigInt);

            var isActive = !result.Paused && result.MaxValidBlockNumber > 0;
            _logger.LogInformation("Upkeep {UpkeepId} active={IsActive}", upkeepId, isActive);
            return isActive;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check upkeep {UpkeepId}", upkeepId);
            return false;
        }
    }

    public async Task<bool> CancelUpkeepAsync(string upkeepId, CancellationToken ct = default)
    {
        _logger.LogInformation("Cancelling upkeep {UpkeepId}", upkeepId);

        var account = EthereumAccountProvider.CreateAccount(_settings.PrivateKeyPath)
            ?? throw new InvalidOperationException("Private key is required to cancel upkeep.");

        var web3 = new Web3(account, _settings.EthereumRpcUrl);
        var contract = web3.Eth.GetContract(AutomationRegistryAbi.Abi, _settings.AutomationRegistryAddress);
        var cancelFunction = contract.GetFunction("cancelUpkeep");

        try
        {
            var upkeepIdBigInt = BigInteger.Parse(upkeepId);
            var txHash = await cancelFunction.SendTransactionAsync(
                account.Address,
                new HexBigInteger(200_000),
                null,
                upkeepIdBigInt
            );

            _logger.LogInformation("Upkeep {UpkeepId} cancelled, txHash={TxHash}", upkeepId, txHash);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel upkeep {UpkeepId}", upkeepId);
            return false;
        }
    }

    [Nethereum.ABI.FunctionEncoding.Attributes.FunctionOutput]
    private class GetUpkeepOutput : Nethereum.ABI.FunctionEncoding.Attributes.IFunctionOutputDTO
    {
        [Nethereum.ABI.FunctionEncoding.Attributes.Parameter("address", "target", 1)]
        public string Target { get; set; } = string.Empty;

        [Nethereum.ABI.FunctionEncoding.Attributes.Parameter("uint32", "executeGas", 2)]
        public uint ExecuteGas { get; set; }

        [Nethereum.ABI.FunctionEncoding.Attributes.Parameter("bytes", "checkData", 3)]
        public byte[] CheckData { get; set; } = Array.Empty<byte>();

        [Nethereum.ABI.FunctionEncoding.Attributes.Parameter("uint96", "balance", 4)]
        public BigInteger Balance { get; set; }

        [Nethereum.ABI.FunctionEncoding.Attributes.Parameter("address", "admin", 5)]
        public string Admin { get; set; } = string.Empty;

        [Nethereum.ABI.FunctionEncoding.Attributes.Parameter("uint64", "maxValidBlocknumber", 6)]
        public ulong MaxValidBlockNumber { get; set; }

        [Nethereum.ABI.FunctionEncoding.Attributes.Parameter("uint32", "lastPerformBlockNumber", 7)]
        public uint LastPerformBlockNumber { get; set; }

        [Nethereum.ABI.FunctionEncoding.Attributes.Parameter("uint96", "amountSpent", 8)]
        public BigInteger AmountSpent { get; set; }

        [Nethereum.ABI.FunctionEncoding.Attributes.Parameter("bool", "paused", 9)]
        public bool Paused { get; set; }
    }
}
