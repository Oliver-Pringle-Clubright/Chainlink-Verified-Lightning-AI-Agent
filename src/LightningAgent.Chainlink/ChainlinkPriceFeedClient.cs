using System.Numerics;
using LightningAgent.Chainlink.Contracts;
using LightningAgent.Core.Configuration;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models.Chainlink;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nethereum.Web3;

namespace LightningAgent.Chainlink;

public class ChainlinkPriceFeedClient : IChainlinkPriceFeedClient
{
    private readonly ChainlinkSettings _settings;
    private readonly ILogger<ChainlinkPriceFeedClient> _logger;

    public ChainlinkPriceFeedClient(
        IOptions<ChainlinkSettings> settings,
        ILogger<ChainlinkPriceFeedClient> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PriceFeedData> GetLatestPriceAsync(string priceFeedAddress, CancellationToken ct = default)
    {
        _logger.LogInformation("Fetching latest price from feed {Address}", priceFeedAddress);

        var web3 = new Web3(_settings.EthereumRpcUrl);
        var contract = web3.Eth.GetContract(PriceFeedAbi.Abi, priceFeedAddress);

        var latestRoundDataFunction = contract.GetFunction("latestRoundData");
        var decimalsFunction = contract.GetFunction("decimals");

        var roundData = await latestRoundDataFunction.CallDeserializingToObjectAsync<LatestRoundDataOutput>();
        var decimals = await decimalsFunction.CallAsync<byte>();

        var divisor = BigInteger.Pow(10, decimals);
        var answer = (decimal)roundData.Answer / (decimal)divisor;

        var priceFeed = new PriceFeedData
        {
            RoundId = (long)roundData.RoundId,
            Answer = answer,
            StartedAt = DateTimeOffset.FromUnixTimeSeconds((long)roundData.StartedAt).UtcDateTime,
            UpdatedAt = DateTimeOffset.FromUnixTimeSeconds((long)roundData.UpdatedAt).UtcDateTime,
            AnsweredInRound = (long)roundData.AnsweredInRound
        };

        _logger.LogInformation("Price feed {Address}: {Price} (round {RoundId})",
            priceFeedAddress, priceFeed.Answer, priceFeed.RoundId);

        return priceFeed;
    }

    [Nethereum.ABI.FunctionEncoding.Attributes.FunctionOutput]
    private class LatestRoundDataOutput : Nethereum.ABI.FunctionEncoding.Attributes.IFunctionOutputDTO
    {
        [Nethereum.ABI.FunctionEncoding.Attributes.Parameter("uint80", "roundId", 1)]
        public BigInteger RoundId { get; set; }

        [Nethereum.ABI.FunctionEncoding.Attributes.Parameter("int256", "answer", 2)]
        public BigInteger Answer { get; set; }

        [Nethereum.ABI.FunctionEncoding.Attributes.Parameter("uint256", "startedAt", 3)]
        public BigInteger StartedAt { get; set; }

        [Nethereum.ABI.FunctionEncoding.Attributes.Parameter("uint256", "updatedAt", 4)]
        public BigInteger UpdatedAt { get; set; }

        [Nethereum.ABI.FunctionEncoding.Attributes.Parameter("uint80", "answeredInRound", 5)]
        public BigInteger AnsweredInRound { get; set; }
    }
}
