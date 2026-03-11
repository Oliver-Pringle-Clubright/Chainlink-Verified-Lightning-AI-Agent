using LightningAgent.Api.Helpers;
using LightningAgent.Core.Models.Lightning;
using LightningAgent.Engine.Services;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace LightningAgent.Api.Controllers;

/// <summary>
/// Admin endpoints for managing Lightning Network channels.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/channels")]
[Route("api/v{version:apiVersion}/channels")]
[Produces("application/json")]
public class ChannelsController : ControllerBase
{
    private readonly ChannelManagerService _channelManager;
    private readonly ILogger<ChannelsController> _logger;

    public ChannelsController(ChannelManagerService channelManager, ILogger<ChannelsController> logger)
    {
        _channelManager = channelManager;
        _logger = logger;
    }

    /// <summary>
    /// List all open Lightning Network channels on the node.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<LndChannel>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IReadOnlyList<LndChannel>>> ListChannels(CancellationToken ct)
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext))
            return Forbid();

        var channels = await _channelManager.ListChannelsAsync(ct);
        return Ok(channels);
    }

    /// <summary>
    /// Get the aggregate channel balance (local and remote) from the Lightning node.
    /// </summary>
    [HttpGet("balance")]
    [ProducesResponseType(typeof(ChannelBalance), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ChannelBalance>> GetChannelBalance(CancellationToken ct)
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext))
            return Forbid();

        var balance = await _channelManager.GetChannelBalanceAsync(ct);
        return Ok(balance);
    }

    /// <summary>
    /// Open a new Lightning Network channel to the specified node.
    /// </summary>
    /// <param name="request">The channel open parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("open")]
    [ProducesResponseType(typeof(OpenChannelResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<OpenChannelResult>> OpenChannel(
        [FromBody] OpenChannelRequest request,
        CancellationToken ct)
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext))
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.NodePubkey))
            return BadRequest("NodePubkey is required.");

        if (request.AmountSats <= 0)
            return BadRequest("AmountSats must be greater than zero.");

        _logger.LogInformation(
            "Admin opening channel to {NodePubkey} with {AmountSats} sats",
            request.NodePubkey, request.AmountSats);

        var result = await _channelManager.OpenChannelAsync(request.NodePubkey, request.AmountSats, ct);
        return Ok(result);
    }

    /// <summary>
    /// Get a curated list of recommended Lightning Network peers for channel opening.
    /// </summary>
    [HttpGet("recommended-peers")]
    [ProducesResponseType(typeof(IReadOnlyList<RecommendedPeer>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IReadOnlyList<RecommendedPeer>>> GetRecommendedPeers()
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext))
            return Forbid();

        var peers = await _channelManager.GetRecommendedPeersAsync();
        return Ok(peers);
    }
}

/// <summary>
/// Request body for opening a new Lightning Network channel.
/// </summary>
public class OpenChannelRequest
{
    /// <summary>
    /// The public key of the remote node to open a channel with.
    /// </summary>
    public string NodePubkey { get; set; } = string.Empty;

    /// <summary>
    /// The amount in satoshis to fund the channel with.
    /// </summary>
    public long AmountSats { get; set; }
}
