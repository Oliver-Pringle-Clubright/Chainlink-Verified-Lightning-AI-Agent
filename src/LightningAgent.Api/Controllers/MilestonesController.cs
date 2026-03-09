using System.Text;
using LightningAgent.Api.DTOs;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Engine.Workflows;
using Microsoft.AspNetCore.Mvc;

namespace LightningAgent.Api.Controllers;

[ApiController]
[Route("api/milestones")]
public class MilestonesController : ControllerBase
{
    private readonly IMilestoneRepository _milestoneRepository;
    private readonly IVerificationRepository _verificationRepository;
    private readonly TaskLifecycleWorkflow _workflow;
    private readonly ITaskOrchestrator _orchestrator;
    private readonly ILogger<MilestonesController> _logger;

    public MilestonesController(
        IMilestoneRepository milestoneRepository,
        IVerificationRepository verificationRepository,
        TaskLifecycleWorkflow workflow,
        ITaskOrchestrator orchestrator,
        ILogger<MilestonesController> logger)
    {
        _milestoneRepository = milestoneRepository;
        _verificationRepository = verificationRepository;
        _workflow = workflow;
        _orchestrator = orchestrator;
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

        byte[] outputBytes;

        // All output data must be valid base64
        try
        {
            outputBytes = Convert.FromBase64String(request.OutputData);
        }
        catch (FormatException)
        {
            return BadRequest("OutputData must be valid base64-encoded data.");
        }

        // If ContentType is not "base64", re-encode as UTF-8 bytes of the raw string
        if (!string.Equals(request.ContentType, "base64", StringComparison.OrdinalIgnoreCase))
        {
            outputBytes = Encoding.UTF8.GetBytes(request.OutputData);
        }

        var passed = await _workflow.ProcessMilestoneSubmissionAsync(id, outputBytes, ct);

        // After processing, check if the parent task can be completed
        bool taskCompleted = await _orchestrator.CheckAndCompleteTaskAsync(milestone.TaskId, ct);

        // Fetch verification results for the response
        var verifications = await _verificationRepository.GetByMilestoneIdAsync(id, ct);
        var updatedMilestone = await _milestoneRepository.GetByIdAsync(id, ct);

        return Ok(new
        {
            milestoneId = id,
            passed,
            milestoneStatus = updatedMilestone?.Status.ToString(),
            verificationResult = updatedMilestone?.VerificationResult,
            taskCompleted,
            verifications = verifications.Select(v => new
            {
                strategyType = v.StrategyType,
                score = v.Score,
                passed = v.Passed,
                details = v.Details
            }),
            message = passed
                ? $"Milestone {id} verification passed."
                : $"Milestone {id} verification failed."
        });
    }
}
