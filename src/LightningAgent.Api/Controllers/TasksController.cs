using LightningAgent.Api.DTOs;
using LightningAgent.Api.Helpers;
using LightningAgent.Core.Configuration;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using LightningAgent.Data;
using LightningAgent.Engine.Workflows;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
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
    private readonly SpendLimitSettings _spendLimits;
    private readonly ISpendLimitService _spendLimitService;
    private readonly ITaskQueue _taskQueue;
    private readonly TaskLifecycleWorkflow _workflow;
    private readonly ILogger<TasksController> _logger;

    public TasksController(
        ITaskRepository taskRepository,
        IMilestoneRepository milestoneRepository,
        ITaskOrchestrator taskOrchestrator,
        INaturalLanguageTaskParser nlParser,
        IOptions<SpendLimitSettings> spendLimitOptions,
        ISpendLimitService spendLimitService,
        ITaskQueue taskQueue,
        TaskLifecycleWorkflow workflow,
        ILogger<TasksController> logger)
    {
        _taskRepository = taskRepository;
        _milestoneRepository = milestoneRepository;
        _taskOrchestrator = taskOrchestrator;
        _nlParser = nlParser;
        _spendLimits = spendLimitOptions.Value;
        _spendLimitService = spendLimitService;
        _taskQueue = taskQueue;
        _workflow = workflow;
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

        if (request.MaxPayoutSats > _spendLimits.DefaultPerTaskMaxSats)
            return BadRequest($"MaxPayoutSats ({request.MaxPayoutSats}) exceeds the per-task limit of {_spendLimits.DefaultPerTaskMaxSats} sats.");

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

        int id;
        try
        {
            id = await _taskRepository.CreateAsync(task, ct);
        }
        catch (SqliteException ex) when (SqliteExceptionHandler.IsUniqueConstraintViolation(ex))
        {
            return Conflict("A task with the same ExternalId already exists.");
        }
        catch (SqliteException ex) when (SqliteExceptionHandler.IsForeignKeyViolation(ex))
        {
            return BadRequest("Invalid foreign key reference in task data.");
        }

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
    public async Task<ActionResult<PaginatedResponse<TaskDetailResponse>>> ListTasks(
        [FromQuery] string? status,
        [FromQuery] int? agentId,
        [FromQuery] string? clientId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        // When agentId or clientId filters are specified, fall back to in-memory pagination
        if (agentId.HasValue)
        {
            var agentTasks = await _taskRepository.GetByAssignedAgentAsync(agentId.Value, ct);
            if (!string.IsNullOrEmpty(clientId))
                agentTasks = agentTasks.Where(t => t.ClientId == clientId).ToList();
            var total = agentTasks.Count;
            var items = agentTasks.Skip((page - 1) * pageSize).Take(pageSize).Select(MapToDetailResponse).ToList();
            return Ok(new PaginatedResponse<TaskDetailResponse>
            {
                Items = items, Page = page, PageSize = pageSize, TotalCount = total
            });
        }

        TaskStatus? statusFilter = null;
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<TaskStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            statusFilter = parsedStatus;
        }

        var offset = (page - 1) * pageSize;
        var totalCount = await _taskRepository.GetCountAsync(statusFilter, ct);
        var tasks = await _taskRepository.GetPagedAsync(offset, pageSize, statusFilter, ct);

        // If clientId filter is specified, apply it in memory (reduces count accuracy but keeps simplicity)
        IEnumerable<TaskItem> filtered = tasks;
        if (!string.IsNullOrEmpty(clientId))
        {
            filtered = tasks.Where(t => t.ClientId == clientId);
        }

        var result = filtered.Select(MapToDetailResponse).ToList();
        return Ok(new PaginatedResponse<TaskDetailResponse>
        {
            Items = result,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
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
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext))
            return Forbid();

        if (body.AgentId <= 0)
            return BadRequest("AgentId must be greater than zero.");

        var task = await _taskRepository.GetByIdAsync(id, ct);
        if (task is null)
            return NotFound($"Task {id} not found.");

        // Check agent spend limit before assignment
        var withinLimit = await _spendLimitService.CheckLimitAsync(body.AgentId, task.MaxPayoutSats, ct);
        if (!withinLimit)
            return BadRequest($"Agent {body.AgentId} has exceeded their spend limit. Cannot assign task with {task.MaxPayoutSats} sats payout.");

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

    [HttpGet("{id:int}/deliverable")]
    public async Task<IActionResult> GetDeliverable(int id, CancellationToken ct)
    {
        var task = await _taskRepository.GetByIdAsync(id, ct);
        if (task is null)
            return NotFound($"Task {id} not found.");

        var deliverable = await _taskOrchestrator.AssembleDeliverableAsync(id, ct);

        if (string.IsNullOrEmpty(deliverable))
            return NotFound($"No deliverable available for task {id}.");

        return Content(deliverable, "text/plain");
    }

    [HttpPost("{id:int}/retry")]
    public async Task<IActionResult> RetryFailedSubtasks(int id, CancellationToken ct)
    {
        var task = await _taskRepository.GetByIdAsync(id, ct);
        if (task is null)
            return NotFound($"Task {id} not found.");

        var subtasks = await _taskRepository.GetSubtasksAsync(id, ct);

        var retriedCount = 0;

        foreach (var subtask in subtasks)
        {
            var milestones = await _milestoneRepository.GetByTaskIdAsync(subtask.Id, ct);

            foreach (var milestone in milestones.Where(m => m.Status == MilestoneStatus.Failed))
            {
                _logger.LogInformation(
                    "Retrying failed milestone {MilestoneId} for subtask {SubtaskId} of task {TaskId}",
                    milestone.Id, subtask.Id, id);

                await _workflow.ProcessRetryAsync(milestone.Id, ct);
                retriedCount++;
            }
        }

        // Also check milestones directly on the parent task
        var parentMilestones = await _milestoneRepository.GetByTaskIdAsync(id, ct);
        foreach (var milestone in parentMilestones.Where(m => m.Status == MilestoneStatus.Failed))
        {
            _logger.LogInformation(
                "Retrying failed milestone {MilestoneId} on task {TaskId}",
                milestone.Id, id);

            await _workflow.ProcessRetryAsync(milestone.Id, ct);
            retriedCount++;
        }

        // Re-set parent task status to InProgress
        await _taskRepository.UpdateStatusAsync(id, TaskStatus.InProgress, ct);

        _logger.LogInformation(
            "Task {TaskId} retry complete: {RetriedCount} milestones retried", id, retriedCount);

        return Ok(new
        {
            taskId = id,
            retriedMilestones = retriedCount,
            message = $"Retried {retriedCount} failed milestone(s). Task status set to InProgress."
        });
    }

    [HttpPost("{id:int}/enqueue")]
    public async Task<IActionResult> EnqueueTask(int id, CancellationToken ct)
    {
        var task = await _taskRepository.GetByIdAsync(id, ct);
        if (task is null)
            return NotFound($"Task {id} not found.");

        if (task.Status is not (TaskStatus.Pending or TaskStatus.Assigned))
            return BadRequest($"Task {id} is in status '{task.Status}' and cannot be enqueued. Only Pending or Assigned tasks can be queued.");

        _logger.LogInformation("Enqueuing task {TaskId} for background orchestration", id);

        await _taskQueue.EnqueueAsync(id, ct);

        return Accepted(new
        {
            taskId = id,
            message = $"Task {id} has been enqueued for background orchestration."
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
