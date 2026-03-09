using System.Text;
using LightningAgent.Api.DTOs;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Engine.Workflows;
using Microsoft.AspNetCore.Mvc;

namespace LightningAgent.Api.Controllers;

[ApiController]
[Route("api/milestones")]
public class MilestonesController : ControllerBase
{
    private readonly IMilestoneRepository _milestoneRepository;
    private readonly TaskLifecycleWorkflow _workflow;
    private readonly ILogger<MilestonesController> _logger;

    public MilestonesController(
        IMilestoneRepository milestoneRepository,
        TaskLifecycleWorkflow workflow,
        ILogger<MilestonesController> logger)
    {
        _milestoneRepository = milestoneRepository;
        _workflow = workflow;
        _logger = logger;
    }

    [HttpGet("by-task/{taskId:int}")]
    public async Task<ActionResult<List<MilestoneDto>>> GetMilestonesByTask(int taskId, CancellationToken ct)
    {
        var milestones = await _milestoneRepository.GetByTaskIdAsync(taskId, ct);

        var result = milestones.Select(m => new MilestoneDto
        {
            Id = m.Id,
            SequenceNumber = m.SequenceNumber,
            Title = m.Title,
            Status = m.Status.ToString(),
            PayoutSats = m.PayoutSats,
            VerifiedAt = m.VerifiedAt,
            PaidAt = m.PaidAt
        }).ToList();

        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<MilestoneDto>> GetMilestone(int id, CancellationToken ct)
    {
        var milestone = await _milestoneRepository.GetByIdAsync(id, ct);
        if (milestone is null)
            return NotFound($"Milestone {id} not found.");

        return Ok(new MilestoneDto
        {
            Id = milestone.Id,
            SequenceNumber = milestone.SequenceNumber,
            Title = milestone.Title,
            Status = milestone.Status.ToString(),
            PayoutSats = milestone.PayoutSats,
            VerifiedAt = milestone.VerifiedAt,
            PaidAt = milestone.PaidAt
        });
    }

    [HttpPost("{id:int}/submit")]
    public async Task<IActionResult> SubmitOutput(
        int id,
        [FromBody] SubmitMilestoneOutputRequest request,
        CancellationToken ct)
    {
        var milestone = await _milestoneRepository.GetByIdAsync(id, ct);
        if (milestone is null)
            return NotFound($"Milestone {id} not found.");

        if (string.IsNullOrWhiteSpace(request.OutputData))
            return BadRequest("OutputData is required.");

        _logger.LogInformation("Milestone {MilestoneId} output submitted, running verification workflow", id);

        var outputBytes = Encoding.UTF8.GetBytes(request.OutputData);
        var passed = await _workflow.ProcessMilestoneSubmissionAsync(id, outputBytes, ct);

        return Ok(new
        {
            milestoneId = id,
            passed,
            message = passed
                ? $"Milestone {id} verification passed."
                : $"Milestone {id} verification failed."
        });
    }
}
