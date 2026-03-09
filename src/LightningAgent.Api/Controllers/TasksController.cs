using LightningAgent.Api.DTOs;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using Microsoft.AspNetCore.Mvc;
using TaskStatus = LightningAgent.Core.Enums.TaskStatus;

namespace LightningAgent.Api.Controllers;

[ApiController]
[Route("api/tasks")]
public class TasksController : ControllerBase
{
    private readonly ITaskRepository _taskRepository;
    private readonly IMilestoneRepository _milestoneRepository;
    private readonly ITaskOrchestrator _taskOrchestrator;
    private readonly INaturalLanguageTaskParser _nlParser;
    private readonly ILogger<TasksController> _logger;

    public TasksController(
        ITaskRepository taskRepository,
        IMilestoneRepository milestoneRepository,
        ITaskOrchestrator taskOrchestrator,
        INaturalLanguageTaskParser nlParser,
        ILogger<TasksController> logger)
    {
        _taskRepository = taskRepository;
        _milestoneRepository = milestoneRepository;
        _taskOrchestrator = taskOrchestrator;
        _nlParser = nlParser;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<CreateTaskResponse>> CreateTask(
        [FromBody] CreateTaskRequest request,
        CancellationToken ct)
    {
        // If natural language mode is requested, parse the description first
        if (request.UseNaturalLanguage && !string.IsNullOrWhiteSpace(request.Description))
        {
            _logger.LogInformation("Parsing natural language task description");
            var spec = await _nlParser.ParseDescriptionAsync(request.Description, ct);

            if (string.IsNullOrWhiteSpace(request.Title) && !string.IsNullOrWhiteSpace(spec.Title))
                request.Title = spec.Title;
            if (string.IsNullOrWhiteSpace(request.TaskType) && !string.IsNullOrWhiteSpace(spec.TaskType))
                request.TaskType = spec.TaskType;
            if (request.MaxPayoutSats <= 0 && spec.Budget.MaxSats > 0)
                request.MaxPayoutSats = spec.Budget.MaxSats;
            if (string.IsNullOrWhiteSpace(request.VerificationCriteria) && !string.IsNullOrWhiteSpace(spec.VerificationRequirements))
                request.VerificationCriteria = spec.VerificationRequirements;
        }

        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest("Title is required.");
        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest("Description is required.");
        if (!Enum.TryParse<TaskType>(request.TaskType, ignoreCase: true, out var taskType))
            return BadRequest($"Invalid TaskType '{request.TaskType}'. Valid values: {string.Join(", ", Enum.GetNames<TaskType>())}");
        if (request.MaxPayoutSats <= 0)
            return BadRequest("MaxPayoutSats must be greater than zero.");

        var externalId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;

        var task = new TaskItem
        {
            ExternalId = externalId,
            ClientId = request.ClientId ?? "anonymous",
            Title = request.Title,
            Description = request.Description,
            TaskType = taskType,
            Status = TaskStatus.Pending,
            VerificationCriteria = request.VerificationCriteria,
            MaxPayoutSats = request.MaxPayoutSats,
            Priority = request.Priority ?? 0,
            CreatedAt = now,
            UpdatedAt = now
        };

        var id = await _taskRepository.CreateAsync(task, ct);

        _logger.LogInformation("Created task {TaskId} (ext: {ExternalId})", id, externalId);

        return Ok(new CreateTaskResponse
        {
            TaskId = id,
            ExternalId = externalId,
            Status = TaskStatus.Pending.ToString(),
            Message = "Task created successfully."
        });
    }

    [HttpGet]
    public async Task<ActionResult<List<TaskDetailResponse>>> ListTasks(
        [FromQuery] string? status,
        [FromQuery] int? agentId,
        [FromQuery] string? clientId,
        CancellationToken ct)
    {
        IReadOnlyList<TaskItem> tasks;

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<TaskStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            tasks = await _taskRepository.GetByStatusAsync(parsedStatus, ct);
        }
        else if (agentId.HasValue)
        {
            tasks = await _taskRepository.GetByAssignedAgentAsync(agentId.Value, ct);
        }
        else
        {
            // Return all pending tasks as a sensible default
            tasks = await _taskRepository.GetByStatusAsync(TaskStatus.Pending, ct);
        }

        // If clientId filter is specified, apply it in memory
        if (!string.IsNullOrEmpty(clientId))
        {
            tasks = tasks.Where(t => t.ClientId == clientId).ToList();
        }

        var result = tasks.Select(MapToDetailResponse).ToList();
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TaskDetailResponse>> GetTask(int id, CancellationToken ct)
    {
        var task = await _taskRepository.GetByIdAsync(id, ct);
        if (task is null)
            return NotFound($"Task {id} not found.");

        var response = MapToDetailResponse(task);

        var milestones = await _milestoneRepository.GetByTaskIdAsync(id, ct);
        response.Milestones = milestones.Select(m => new MilestoneDto
        {
            Id = m.Id,
            SequenceNumber = m.SequenceNumber,
            Title = m.Title,
            Status = m.Status.ToString(),
            PayoutSats = m.PayoutSats,
            VerifiedAt = m.VerifiedAt,
            PaidAt = m.PaidAt
        }).ToList();

        return Ok(response);
    }

    [HttpPost("{id:int}/assign")]
    public async Task<IActionResult> AssignAgent(
        int id,
        [FromBody] AssignAgentBody body,
        CancellationToken ct)
    {
        var task = await _taskRepository.GetByIdAsync(id, ct);
        if (task is null)
            return NotFound($"Task {id} not found.");

        task.AssignedAgentId = body.AgentId;
        task.Status = TaskStatus.Assigned;
        task.UpdatedAt = DateTime.UtcNow;

        await _taskRepository.UpdateAsync(task, ct);

        _logger.LogInformation("Task {TaskId} assigned to agent {AgentId}", id, body.AgentId);

        return Ok(new { message = $"Task {id} assigned to agent {body.AgentId}." });
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> CancelTask(int id, CancellationToken ct)
    {
        var task = await _taskRepository.GetByIdAsync(id, ct);
        if (task is null)
            return NotFound($"Task {id} not found.");

        await _taskRepository.UpdateStatusAsync(id, TaskStatus.Failed, ct);

        _logger.LogInformation("Task {TaskId} cancelled", id);

        return Ok(new { message = $"Task {id} cancelled." });
    }

    [HttpGet("{id:int}/subtasks")]
    public async Task<ActionResult<List<TaskDetailResponse>>> GetSubtasks(int id, CancellationToken ct)
    {
        var subtasks = await _taskRepository.GetSubtasksAsync(id, ct);
        var result = subtasks.Select(MapToDetailResponse).ToList();
        return Ok(result);
    }

    [HttpPost("{id:int}/orchestrate")]
    public async Task<IActionResult> OrchestrateTask(int id, CancellationToken ct)
    {
        var task = await _taskRepository.GetByIdAsync(id, ct);
        if (task is null)
            return NotFound($"Task {id} not found.");

        _logger.LogInformation("Starting orchestration for task {TaskId}", id);

        var orchestrated = await _taskOrchestrator.OrchestrateTaskAsync(task, ct);

        return Ok(new
        {
            taskId = orchestrated.Id,
            status = orchestrated.Status.ToString(),
            assignedAgentId = orchestrated.AssignedAgentId,
            message = $"Task {id} orchestration complete."
        });
    }

    private static TaskDetailResponse MapToDetailResponse(TaskItem t) => new()
    {
        Id = t.Id,
        ExternalId = t.ExternalId,
        Title = t.Title,
        Description = t.Description,
        TaskType = t.TaskType.ToString(),
        Status = t.Status.ToString(),
        MaxPayoutSats = t.MaxPayoutSats,
        ActualPayoutSats = t.ActualPayoutSats,
        PriceUsd = t.PriceUsd,
        AssignedAgentId = t.AssignedAgentId,
        Priority = t.Priority,
        CreatedAt = t.CreatedAt
    };
}

public class AssignAgentBody
{
    public int AgentId { get; set; }
}
