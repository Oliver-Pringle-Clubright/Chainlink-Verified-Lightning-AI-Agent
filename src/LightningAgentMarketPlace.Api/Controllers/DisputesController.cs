using System.ComponentModel.DataAnnotations;
using LightningAgentMarketPlace.Api.DTOs;
using LightningAgentMarketPlace.Core.Enums;
using LightningAgentMarketPlace.Core.Interfaces.Data;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using LightningAgentMarketPlace.Core.Models;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace LightningAgentMarketPlace.Api.Controllers;

/// <summary>
/// Manages dispute creation and resolution.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/disputes")]
[Route("api/v{version:apiVersion}/disputes")]
[Produces("application/json")]
public class DisputesController : ControllerBase
{
    private readonly IDisputeRepository _disputeRepository;
    private readonly ITaskRepository _taskRepository;
    private readonly IMilestoneRepository _milestoneRepository;
    private readonly IClaudeAiClient _claudeAiClient;
    private readonly ILogger<DisputesController> _logger;

    public DisputesController(
        IDisputeRepository disputeRepository,
        ITaskRepository taskRepository,
        IMilestoneRepository milestoneRepository,
        IClaudeAiClient claudeAiClient,
        ILogger<DisputesController> logger)
    {
        _disputeRepository = disputeRepository;
        _taskRepository = taskRepository;
        _milestoneRepository = milestoneRepository;
        _claudeAiClient = claudeAiClient;
        _logger = logger;
    }

    /// <summary>
    /// Open a new dispute for a task or milestone.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Dispute), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Dispute>> OpenDispute(
        [FromBody] OpenDisputeRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest("Reason is required.");
        if (string.IsNullOrWhiteSpace(request.InitiatedBy))
            return BadRequest("InitiatedBy is required.");
        if (string.IsNullOrWhiteSpace(request.InitiatorId))
            return BadRequest("InitiatorId is required.");

        var now = DateTime.UtcNow;
        var dispute = new Dispute
        {
            TaskId = request.TaskId,
            MilestoneId = request.MilestoneId,
            InitiatedBy = request.InitiatedBy,
            InitiatorId = request.InitiatorId,
            Reason = request.Reason,
            Status = DisputeStatus.Open,
            AmountDisputedSats = request.AmountDisputedSats,
            CreatedAt = now
        };

        var id = await _disputeRepository.CreateAsync(dispute, ct);
        dispute.Id = id;

        _logger.LogInformation("Opened dispute {DisputeId} for task {TaskId}", id, request.TaskId);

        return Ok(dispute);
    }

    /// <summary>
    /// Get a single dispute by ID.
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(Dispute), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Dispute>> GetDispute(int id, CancellationToken ct)
    {
        var dispute = await _disputeRepository.GetByIdAsync(id, ct);
        if (dispute is null)
            return NotFound($"Dispute {id} not found.");

        return Ok(dispute);
    }

    /// <summary>
    /// Resolve an open dispute with a resolution message.
    /// </summary>
    [HttpPost("{id:int}/resolve")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ResolveDispute(
        int id,
        [FromBody] ResolveDisputeBody body,
        CancellationToken ct)
    {
        var dispute = await _disputeRepository.GetByIdAsync(id, ct);
        if (dispute is null)
            return NotFound($"Dispute {id} not found.");

        if (string.IsNullOrWhiteSpace(body.Resolution))
            return BadRequest("Resolution is required.");

        dispute.Status = DisputeStatus.Resolved;
        dispute.Resolution = body.Resolution;
        dispute.ResolvedAt = DateTime.UtcNow;

        await _disputeRepository.UpdateAsync(dispute, ct);

        _logger.LogInformation("Dispute {DisputeId} resolved", id);

        return Ok(new { message = $"Dispute {id} resolved." });
    }

    /// <summary>
    /// Request AI arbitration for an open dispute.
    /// Calls Claude AI to review the dispute context and provide a recommendation.
    /// Does NOT auto-resolve: a human still decides.
    /// </summary>
    [HttpPost("{id:int}/arbitrate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ArbitrateDispute(int id, CancellationToken ct)
    {
        var dispute = await _disputeRepository.GetByIdAsync(id, ct);
        if (dispute is null)
            return NotFound($"Dispute {id} not found.");

        if (dispute.Status != DisputeStatus.Open)
            return BadRequest($"Dispute {id} is not open (current status: {dispute.Status}).");

        // Load the task context
        var task = await _taskRepository.GetByIdAsync(dispute.TaskId, ct);
        if (task is null)
            return NotFound($"Task {dispute.TaskId} referenced by dispute not found.");

        // Load milestone output if the dispute is about a milestone
        string milestoneContext = "No specific milestone associated with this dispute.";
        if (dispute.MilestoneId.HasValue)
        {
            var milestone = await _milestoneRepository.GetByIdAsync(dispute.MilestoneId.Value, ct);
            if (milestone is not null)
            {
                milestoneContext = $"Milestone #{milestone.Id} (Sequence {milestone.SequenceNumber}): {milestone.Title}\n" +
                                  $"Verification Criteria: {milestone.VerificationCriteria}\n" +
                                  $"Status: {milestone.Status}\n" +
                                  $"Output Data: {(string.IsNullOrEmpty(milestone.OutputData) ? "(none submitted)" : milestone.OutputData)}\n" +
                                  $"Verification Result: {(string.IsNullOrEmpty(milestone.VerificationResult) ? "(none)" : milestone.VerificationResult)}";
            }
        }

        var systemPrompt = @"You are an impartial AI arbitrator for a task marketplace that pays agents via Lightning Network.
Your role is to review dispute details and provide a fair, reasoned recommendation.
Consider:
- Whether the task description was clear enough
- Whether the agent's output meets the verification criteria
- Whether the dispute reason is valid
- The amount of sats in dispute
Provide your recommendation in a structured format:
1. Summary of the dispute
2. Analysis of both sides
3. Recommended resolution (favor client, favor agent, or split)
4. Reasoning
Be concise but thorough. Do NOT make a final binding decision - a human administrator will review your recommendation.";

        var userMessage = $@"=== DISPUTE #{dispute.Id} ===
Amount Disputed: {dispute.AmountDisputedSats} sats
Initiated By: {dispute.InitiatedBy} (ID: {dispute.InitiatorId})
Dispute Reason: {dispute.Reason}

=== TASK CONTEXT ===
Task #{task.Id}: {task.Title}
Description: {task.Description}
Task Type: {task.TaskType}
Verification Criteria: {task.VerificationCriteria ?? "(none specified)"}
Max Payout: {task.MaxPayoutSats} sats
Status: {task.Status}

=== MILESTONE CONTEXT ===
{milestoneContext}

Please provide your arbitration recommendation.";

        try
        {
            var recommendation = await _claudeAiClient.SendMessageAsync(systemPrompt, userMessage, ct);

            _logger.LogInformation(
                "AI arbitration completed for dispute {DisputeId}. Recommendation generated.",
                id);

            return Ok(new
            {
                disputeId = id,
                status = "recommendation_generated",
                recommendation,
                note = "This is an AI recommendation only. A human administrator must review and decide the final resolution."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI arbitration failed for dispute {DisputeId}", id);
            return StatusCode(500, new { message = "AI arbitration failed. Please try again or resolve manually.", error = ex.Message });
        }
    }
}

public class ResolveDisputeBody
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(5000, MinimumLength = 1)]
    public string Resolution { get; set; } = string.Empty;
}
