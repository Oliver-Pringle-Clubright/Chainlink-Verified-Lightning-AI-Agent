using Asp.Versioning;
using LightningAgent.Api.Helpers;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models.Chainlink;
using LightningAgent.Engine.Services;
using Microsoft.AspNetCore.Mvc;

namespace LightningAgent.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/ccip")]
public class CcipController : ControllerBase
{
    private readonly CcipBridgeService _bridge;
    private readonly ICcipMessageRepository _ccipRepo;
    private readonly ILogger<CcipController> _logger;

    public CcipController(
        CcipBridgeService bridge,
        ICcipMessageRepository ccipRepo,
        ILogger<CcipController> logger)
    {
        _bridge = bridge;
        _ccipRepo = ccipRepo;
        _logger = logger;
    }

    /// <summary>
    /// Returns the list of known CCIP-supported chains with router addresses.
    /// </summary>
    [HttpGet("chains")]
    public IActionResult GetSupportedChains()
    {
        return Ok(CcipBridgeService.GetKnownChains());
    }

    /// <summary>
    /// Checks if a specific chain selector is supported by the configured CCIP router.
    /// </summary>
    [HttpGet("chains/{chainSelector}/supported")]
    public async Task<IActionResult> IsChainSupported(ulong chainSelector, CancellationToken ct)
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext))
            return Unauthorized(new { detail = "Admin access required." });

        var supported = await _bridge.IsChainSupportedAsync(chainSelector, ct);
        return Ok(new { chainSelector, supported });
    }

    /// <summary>
    /// Estimates the CCIP fee for sending a message to a destination chain.
    /// </summary>
    [HttpPost("estimate-fee")]
    public async Task<IActionResult> EstimateFee([FromBody] EstimateFeeRequest request, CancellationToken ct)
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext))
            return Unauthorized(new { detail = "Admin access required." });

        var payload = string.IsNullOrEmpty(request.PayloadHex)
            ? Array.Empty<byte>()
            : Convert.FromHexString(request.PayloadHex);

        var estimate = await _bridge.EstimateFeeAsync(
            request.DestinationChainSelector,
            request.ReceiverAddress,
            payload,
            ct);

        return Ok(estimate);
    }

    /// <summary>
    /// Sends a cross-chain message (arbitrary data payload) via CCIP.
    /// </summary>
    [HttpPost("send-message")]
    public async Task<IActionResult> SendMessage([FromBody] SendCcipMessageRequest request, CancellationToken ct)
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext))
            return Unauthorized(new { detail = "Admin access required." });

        var payload = string.IsNullOrEmpty(request.PayloadHex)
            ? System.Text.Encoding.UTF8.GetBytes(request.PayloadText ?? "")
            : Convert.FromHexString(request.PayloadHex);

        var message = await _bridge.SendTaskAssignmentAsync(
            request.DestinationChainSelector,
            request.ReceiverAddress,
            request.TaskId ?? 0,
            request.AgentId ?? 0,
            new { data = Convert.ToBase64String(payload), description = request.Description },
            ct);

        return Ok(new { messageId = message.MessageId, txHash = message.TxHash, status = message.Status });
    }

    /// <summary>
    /// Sends a cross-chain token transfer via CCIP.
    /// </summary>
    [HttpPost("send-tokens")]
    public async Task<IActionResult> SendTokens([FromBody] SendCcipTokenRequest request, CancellationToken ct)
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext))
            return Unauthorized(new { detail = "Admin access required." });

        var message = await _bridge.SendPaymentAsync(
            request.DestinationChainSelector,
            request.ReceiverAddress,
            request.TokenAddress,
            request.AmountWei,
            request.TaskId,
            request.AgentId,
            ct);

        return Ok(new { messageId = message.MessageId, txHash = message.TxHash, status = message.Status });
    }

    /// <summary>
    /// Sends a verification proof cross-chain via CCIP.
    /// </summary>
    [HttpPost("send-verification")]
    public async Task<IActionResult> SendVerification([FromBody] SendCcipVerificationRequest request, CancellationToken ct)
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext))
            return Unauthorized(new { detail = "Admin access required." });

        var message = await _bridge.SendVerificationProofAsync(
            request.DestinationChainSelector,
            request.ReceiverAddress,
            request.TaskId,
            request.ProofHash,
            request.Score,
            request.Passed,
            ct);

        return Ok(new { messageId = message.MessageId, txHash = message.TxHash, status = message.Status });
    }

    /// <summary>
    /// Gets the status of a CCIP message by its message ID.
    /// </summary>
    [HttpGet("messages/{messageId}")]
    public async Task<IActionResult> GetMessage(string messageId, CancellationToken ct)
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext) && AuthorizationHelper.GetAuthenticatedAgentId(HttpContext) is null)
            return Unauthorized(new { detail = "Authentication required." });

        var message = await _ccipRepo.GetByMessageIdAsync(messageId, ct);
        if (message is null)
            return NotFound(new { detail = "CCIP message not found." });

        return Ok(message);
    }

    /// <summary>
    /// Lists recent CCIP messages.
    /// </summary>
    [HttpGet("messages")]
    public async Task<IActionResult> GetMessages([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext) && AuthorizationHelper.GetAuthenticatedAgentId(HttpContext) is null)
            return Unauthorized(new { detail = "Authentication required." });

        var messages = await _ccipRepo.GetRecentAsync(Math.Min(limit, 200), ct);
        return Ok(messages);
    }

    /// <summary>
    /// Lists CCIP messages associated with a specific task.
    /// </summary>
    [HttpGet("tasks/{taskId}/messages")]
    public async Task<IActionResult> GetTaskMessages(int taskId, CancellationToken ct)
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext) && AuthorizationHelper.GetAuthenticatedAgentId(HttpContext) is null)
            return Unauthorized(new { detail = "Authentication required." });

        var messages = await _ccipRepo.GetByTaskIdAsync(taskId, ct);
        return Ok(messages);
    }
}

// ── Request DTOs ──────────────────────────────────────────────────────

public record EstimateFeeRequest
{
    public ulong DestinationChainSelector { get; init; }
    public string ReceiverAddress { get; init; } = "";
    public string? PayloadHex { get; init; }
}

public record SendCcipMessageRequest
{
    public ulong DestinationChainSelector { get; init; }
    public string ReceiverAddress { get; init; } = "";
    public string? PayloadHex { get; init; }
    public string? PayloadText { get; init; }
    public string? Description { get; init; }
    public int? TaskId { get; init; }
    public int? AgentId { get; init; }
}

public record SendCcipTokenRequest
{
    public ulong DestinationChainSelector { get; init; }
    public string ReceiverAddress { get; init; } = "";
    public string TokenAddress { get; init; } = "";
    public long AmountWei { get; init; }
    public int? TaskId { get; init; }
    public int? AgentId { get; init; }
}

public record SendCcipVerificationRequest
{
    public ulong DestinationChainSelector { get; init; }
    public string ReceiverAddress { get; init; } = "";
    public int TaskId { get; init; }
    public string ProofHash { get; init; } = "";
    public double Score { get; init; }
    public bool Passed { get; init; }
}
