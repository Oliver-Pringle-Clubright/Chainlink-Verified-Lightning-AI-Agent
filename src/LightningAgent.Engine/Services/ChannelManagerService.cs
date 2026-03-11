using LightningAgent.Core.Configuration;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models.Lightning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightningAgent.Engine.Services;

/// <summary>
/// Manages Lightning Network channels: balance queries, channel listing,
/// channel opening, and recommended peer discovery.
/// </summary>
public class ChannelManagerService
{
    private readonly ILightningClient _lightningClient;
    private readonly LightningSettings _settings;
    private readonly ILogger<ChannelManagerService> _logger;

    /// <summary>
    /// Well-known Lightning Network routing nodes recommended for channel opening.
    /// </summary>
    private static readonly IReadOnlyList<RecommendedPeer> WellKnownPeers = new List<RecommendedPeer>
    {
        new()
        {
            Name = "ACINQ",
            PubKey = "03864ef025fde8fb587d989186ce6a4a186895ee44a926bfc370e2c366597a3f8f",
            Description = "ACINQ — operators of the Phoenix wallet and major Lightning routing node"
        },
        new()
        {
            Name = "Bitfinex",
            PubKey = "033d8656219478701227199cbd6f670335c8d408a92ae88b962c49d4dc0e83e025",
            Description = "Bitfinex — major cryptocurrency exchange with high-capacity Lightning channels"
        },
        new()
        {
            Name = "LNBig",
            PubKey = "034ea80f8b148c750463546bd999bf7321a0e6dfc60aaf84571bf1e28a4ceedf37",
            Description = "LNBig — one of the largest Lightning Network liquidity providers"
        },
        new()
        {
            Name = "River Financial",
            PubKey = "03037dc08e9ac63b82581f79b662a4d0ceca8a8ca162b1af3551595b8f2d97b70a",
            Description = "River Financial — well-connected US-based Bitcoin financial services node"
        },
        new()
        {
            Name = "WalletOfSatoshi",
            PubKey = "035e4ff418fc8b5554c5d9eea66396c227bd3a1a07c78e4f8cedc07ba712cb3c16",
            Description = "Wallet of Satoshi — popular Lightning wallet provider with strong routing"
        }
    };

    public ChannelManagerService(
        ILightningClient lightningClient,
        IOptions<LightningSettings> settings,
        ILogger<ChannelManagerService> logger)
    {
        _lightningClient = lightningClient;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves the aggregate channel balance (local and remote) from the LND node.
    /// </summary>
    public async Task<ChannelBalance> GetChannelBalanceAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Fetching channel balance via ChannelManagerService");

        var balance = await _lightningClient.GetChannelBalanceAsync(ct);

        _logger.LogInformation(
            "Channel balance: local={LocalSats} sats, remote={RemoteSats} sats",
            balance.LocalBalanceSats, balance.RemoteBalanceSats);

        return balance;
    }

    /// <summary>
    /// Lists all active channels on the LND node.
    /// </summary>
    public async Task<IReadOnlyList<LndChannel>> ListChannelsAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Listing channels via ChannelManagerService");

        var channels = await _lightningClient.ListChannelsAsync(ct);

        _logger.LogInformation(
            "Found {Count} channels ({ActiveCount} active)",
            channels.Count,
            channels.Count(c => c.Active));

        return channels;
    }

    /// <summary>
    /// Opens a new channel to the specified node with the given local funding amount.
    /// </summary>
    public async Task<OpenChannelResult> OpenChannelAsync(
        string nodePubkey,
        long localAmountSats,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Opening channel to {NodePubkey} with {Amount} sats",
            nodePubkey, localAmountSats);

        var result = await _lightningClient.OpenChannelAsync(nodePubkey, localAmountSats, ct);

        _logger.LogInformation(
            "Channel opened: fundingTxId={FundingTxId}, outputIndex={OutputIndex}",
            result.FundingTxId, result.OutputIndex);

        return result;
    }

    /// <summary>
    /// Returns a curated list of well-known Lightning Network routing nodes
    /// that are recommended for establishing new channels.
    /// </summary>
    public Task<IReadOnlyList<RecommendedPeer>> GetRecommendedPeersAsync()
    {
        _logger.LogDebug("Returning {Count} recommended peers", WellKnownPeers.Count);
        return Task.FromResult(WellKnownPeers);
    }
}
