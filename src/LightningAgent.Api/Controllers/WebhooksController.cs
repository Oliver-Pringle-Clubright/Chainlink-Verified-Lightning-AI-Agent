using System.ComponentModel.DataAnnotations;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace LightningAgent.Api.Controllers;

/// <summary>
/// Manages webhook subscriptions for event notifications.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/webhooks")]
[Route("api/v{version:apiVersion}/webhooks")]
[Produces("application/json")]
public class WebhooksController : ControllerBase
{
    private readonly IWebhookRepository _webhookRepository;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(
        IWebhookRepository webhookRepository,
        ILogger<WebhooksController> logger)
    {
        _webhookRepository = webhookRepository;
        _logger = logger;
    }

    /// <summary>
    /// Register a new webhook subscription.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(WebhookSubscription), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WebhookSubscription>> Register(
        [FromBody] RegisterWebhookRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
            return BadRequest("Url is required.");

        if (string.IsNullOrWhiteSpace(request.Events))
            return BadRequest("Events is required (comma-separated list, e.g. TaskAssigned,MilestoneVerified,PaymentSent).");

        var subscription = new WebhookSubscription
        {
            AgentId = request.AgentId,
            Url = request.Url,
            Events = request.Events,
            Secret = request.Secret,
            Active = true,
            CreatedAt = DateTime.UtcNow
        };

        var id = await _webhookRepository.CreateAsync(subscription, ct);
        subscription.Id = id;

        _logger.LogInformation(
            "Webhook subscription created: {WebhookId} for agent {AgentId} -> {Url} (events: {Events})",
            id, request.AgentId, request.Url, request.Events);

        return Ok(subscription);
    }

    /// <summary>
    /// List webhook subscriptions, optionally filtered by agentId.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<WebhookSubscription>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WebhookSubscription>>> List(
        [FromQuery] int? agentId,
        CancellationToken ct)
    {
        if (agentId.HasValue)
        {
            var subs = await _webhookRepository.GetByAgentIdAsync(agentId.Value, ct);
            return Ok(subs);
        }

        // Without agentId, return empty list (require filter for security)
        return Ok(Array.Empty<WebhookSubscription>());
    }

    /// <summary>
    /// Remove a webhook subscription.
    /// </summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var existing = await _webhookRepository.GetByIdAsync(id, ct);
        if (existing is null)
            return NotFound($"Webhook subscription {id} not found.");

        await _webhookRepository.DeleteAsync(id, ct);

        _logger.LogInformation("Webhook subscription {WebhookId} deleted", id);

        return Ok(new { message = $"Webhook subscription {id} deleted." });
    }
}

public class RegisterWebhookRequest
{
    public int? AgentId { get; set; }

    [Required]
    public string Url { get; set; } = "";

    [Required]
    public string Events { get; set; } = "";

    public string? Secret { get; set; }
}
