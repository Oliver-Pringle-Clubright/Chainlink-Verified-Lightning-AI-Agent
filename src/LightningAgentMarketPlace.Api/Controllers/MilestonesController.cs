using System.Text;
using LightningAgentMarketPlace.Api.DTOs;
using LightningAgentMarketPlace.Api.Helpers;
using LightningAgentMarketPlace.Core.Enums;
using LightningAgentMarketPlace.Core.Interfaces.Data;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using LightningAgentMarketPlace.Engine.Workflows;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace LightningAgentMarketPlace.Api.Controllers;

/// <summary>
/// Manages milestone retrieval, output submission, and verification.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/milestones")]
[Route("api/v{version:apiVersion}/milestones")]
[Produces("application/json")]
public class MilestonesController : ControllerBase
{
    private readonly IMilestoneRepository _milestoneRepository;
    private readonly IVerificationRepository _verificationRepository;
    private readonly ITaskRepository _taskRepository;
    private readonly TaskLifecycleWorkflow _workflow;
    private readonly ITaskOrchestrator _orchestrator;
    private readonly ILogger<MilestonesController> _logger;

    public MilestonesController(
        IMilestoneRepository milestoneRepository,
        IVerificationRepository verificationRepository,
        ITaskRepository taskRepository,
        TaskLifecycleWorkflow workflow,
        ITaskOrchestrator orchestrator,
        ILogger<MilestonesController> logger)
    {
        _milestoneRepository = milestoneRepository;
        _verificationRepository = verificationRepository;
        _taskRepository = taskRepository;
        _workflow = workflow;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    /// <summary>
    /// Get all milestones for a given task.
    /// </summary>
    [HttpGet("by-task/{taskId:int}")]
    [ProducesResponseType(typeof(List<MilestoneDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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

    /// <summary>
    /// Get a single milestone by ID.
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(MilestoneDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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

    /// <summary>
    /// Get the decoded output data for a milestone.
    /// </summary>
    [HttpGet("{id:int}/output")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetMilestoneOutput(int id, CancellationToken ct)
    {
        var milestone = await _milestoneRepository.GetByIdAsync(id, ct);
        if (milestone is null)
            return NotFound($"Milestone {id} not found.");

        if (string.IsNullOrEmpty(milestone.OutputData))
            return NotFound($"Milestone {id} has no output data.");

        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(milestone.OutputData));
        return Content(decoded, "text/plain");
    }

    /// <summary>
    /// Submit output for a milestone and trigger verification.
    /// </summary>
    [HttpPost("{id:int}/submit")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SubmitOutput(
        int id,
        [FromBody] SubmitMilestoneOutputRequest request,
        CancellationToken ct)
    {
        var milestone = await _milestoneRepository.GetByIdAsync(id, ct);
        if (milestone is null)
            return NotFound($"Milestone {id} not found.");

        // Authorization: verify the authenticated agent is assigned to this milestone's task
        var task = await _taskRepository.GetByIdAsync(milestone.TaskId, ct);
        if (task?.AssignedAgentId is int assignedAgentId)
        {
            if (!AuthorizationHelper.CanAccessAgent(HttpContext, assignedAgentId))
                return Forbid();
        }

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
