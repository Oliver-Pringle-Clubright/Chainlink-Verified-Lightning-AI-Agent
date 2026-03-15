using LightningAgent.Core.Configuration;
using LightningAgent.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightningAgent.Engine.Services;

/// <summary>
/// Reads Chainlink price feeds from multiple chains simultaneously.
/// Uses the ChainlinkAddressRegistry to find the correct feed addresses per chain.
/// </summary>
public class MultiChainPriceService
{
    private readonly IChainlinkPriceFeedClient _priceFeed;
    private readonly MultiChainSettings _settings;
    private readonly IPricingService _pricingService;
    private readonly ILogger<MultiChainPriceService> _logger;

    public MultiChainPriceService(
        IChainlinkPriceFeedClient priceFeed,
        IOptions<MultiChainSettings> settings,
        IPricingService pricingService,
        ILogger<MultiChainPriceService> logger)
    {
        _priceFeed = priceFeed;
        _settings = settings.Value;
        _pricingService = pricingService;
        _logger = logger;
    }

    /// <summary>
    /// Refreshes price feeds from all configured secondary chains.
    /// Only reads feeds that exist on each chain (per the registry).
    /// </summary>
    public async Task RefreshAllChainsAsync(CancellationToken ct = default)
    {
        if (!_settings.Enabled || _settings.Chains.Count == 0)
            return;

        foreach (var (name, chain) in _settings.Chains)
        {
            ct.ThrowIfCancellationRequested();

            var defaults = ChainlinkAddressRegistry.GetDefaults(chain.ChainId);
            if (defaults is null || string.IsNullOrEmpty(chain.RpcUrl))
            {
                _logger.LogDebug("Skipping chain {Name} (chainId={ChainId}): no registry entry or RPC URL",
                    name, chain.ChainId);
                continue;
            }

            var chainName = ChainlinkAddressRegistry.GetChainName(chain.ChainId);

            // Read ETH/USD from this chain if available
            if (!string.IsNullOrEmpty(defaults.EthUsdPriceFeedAddress))
            {
                await ReadFeedSafe($"ETH/USD ({chainName})", defaults.EthUsdPriceFeedAddress, chain.RpcUrl, ct);
            }

            // Read BTC/USD if available
            if (!string.IsNullOrEmpty(defaults.BtcUsdPriceFeedAddress))
            {
                await ReadFeedSafe($"BTC/USD ({chainName})", defaults.BtcUsdPriceFeedAddress, chain.RpcUrl, ct);
            }

            // Read LINK/USD if available
            if (!string.IsNullOrEmpty(defaults.LinkUsdPriceFeedAddress))
            {
                await ReadFeedSafe($"LINK/USD ({chainName})", defaults.LinkUsdPriceFeedAddress, chain.RpcUrl, ct);
            }
        }
    }

    /// <summary>
    /// Returns which chains are configured and their status.
    /// </summary>
    public List<ChainStatus> GetConfiguredChains()
    {
        var result = new List<ChainStatus>();
        foreach (var (name, chain) in _settings.Chains)
        {
            result.Add(new ChainStatus
            {
                Name = name,
                ChainId = chain.ChainId,
                ChainName = ChainlinkAddressRegistry.GetChainName(chain.ChainId),
                HasRpcUrl = !string.IsNullOrEmpty(chain.RpcUrl),
                IsMainnet = ChainlinkAddressRegistry.IsMainnet(chain.ChainId)
            });
        }
        return result;
    }

    private async Task ReadFeedSafe(string pair, string feedAddress, string rpcUrl, CancellationToken ct)
    {
        try
        {
            var data = await _priceFeed.GetLatestPriceAsync(feedAddress, rpcUrl, ct);
            _logger.LogInformation("Multi-chain price {Pair}: ${Price:F2}", pair, data.Answer);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read {Pair} from secondary chain", pair);
        }
    }

    public record ChainStatus
    {
        public string Name { get; init; } = "";
        public long ChainId { get; init; }
        public string ChainName { get; init; } = "";
        public bool HasRpcUrl { get; init; }
        public bool IsMainnet { get; init; }
    }
}
