using LightningAgent.Api.DTOs;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace LightningAgent.Api.Controllers;

[ApiController]
[Route("api/disputes")]
public class DisputesController : ControllerBase
{
    private readonly IDisputeRepository _disputeRepository;
    private readonly ILogger<DisputesController> _logger;

    public DisputesController(
        IDisputeRepository disputeRepository,
        ILogger<DisputesController> logger)
    {
        _disputeRepository = disputeRepository;
        _logger = logger;
    }

    [HttpPost]
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

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Dispute>> GetDispute(int id, CancellationToken ct)
    {
        var dispute = await _disputeRepository.GetByIdAsync(id, ct);
        if (dispute is null)
            return NotFound($"Dispute {id} not found.");

        return Ok(dispute);
    }

    [HttpPost("{id:int}/resolve")]
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
}

public class ResolveDisputeBody
{
    public string Resolution { get; set; } = string.Empty;
}
