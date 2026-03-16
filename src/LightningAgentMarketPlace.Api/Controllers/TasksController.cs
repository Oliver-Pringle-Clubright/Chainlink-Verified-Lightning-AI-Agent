using System.ComponentModel.DataAnnotations;
using LightningAgentMarketPlace.Api.DTOs;
using LightningAgentMarketPlace.Api.Helpers;
using LightningAgentMarketPlace.Core.Configuration;
using LightningAgentMarketPlace.Core.Enums;
using LightningAgentMarketPlace.Core.Interfaces.Data;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using LightningAgentMarketPlace.Core.Models;
using LightningAgentMarketPlace.Data;
using LightningAgentMarketPlace.Engine.Workflows;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using TaskStatus = LightningAgentMarketPlace.Core.Enums.TaskStatus;

namespace LightningAgentMarketPlace.Api.Controllers;

/// <summary>
/// Manages task creation, assignment, orchestration, and lifecycle.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/tasks")]
[Route("api/v{version:apiVersion}/tasks")]
[Produces("application/json")]
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
    private readonly IMetricsCollector _metrics;
    private readonly PlatformFeeSettings _feeSettings;
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
        IMetricsCollector metrics,
        IOptions<PlatformFeeSettings> feeSettings,
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
        _metrics = metrics;
        _feeSettings = feeSettings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Create a new task, optionally parsing a natural language description.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreateTaskResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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

        // Ensure budget covers the posting fee
        var postingFee = _feeSettings.TaskPostingFeeSats;
        if (postingFee > 0 && request.MaxPayoutSats <= postingFee)
            return BadRequest($"MaxPayoutSats must be greater than the task posting fee ({postingFee} sats).");

        // Validate task dependency if provided
        if (request.DependsOnTaskId.HasValue)
        {
            var dependencyTask = await _taskRepository.GetByIdAsync(request.DependsOnTaskId.Value, ct);
            if (dependencyTask is null)
                return BadRequest($"Dependency task {request.DependsOnTaskId.Value} does not exist.");
        }

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
            DependsOnTaskId = request.DependsOnTaskId,
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

        _logger.LogInformation(
            "Created task {TaskId} (ext: {ExternalId}), posting fee: {Fee} sats",
            id, externalId, postingFee);
        _metrics.IncrementTasksCreated();

        return Ok(new CreateTaskResponse
        {
            TaskId = id,
            ExternalId = externalId,
            Status = TaskStatus.Pending.ToString(),
            Message = postingFee > 0
                ? $"Task created successfully. Posting fee: {postingFee} sats. Platform commission: {_feeSettings.CommissionRate:P0} on payouts."
                : "Task created successfully."
        });
    }

    /// <summary>
    /// List tasks with optional filters and cursor-based pagination.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<TaskDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PaginatedResponse<TaskDetailResponse>>> ListTasks(
        [FromQuery] string? status,
        [FromQuery] int? agentId,
        [FromQuery] string? clientId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] int? cursor = null,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        TaskStatus? statusFilter = null;
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<TaskStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            statusFilter = parsedStatus;
        }

        var offset = (page - 1) * pageSize;
        var totalCount = await _taskRepository.GetFilteredCountAsync(statusFilter, agentId, clientId, ct);
        var tasks = await _taskRepository.GetFilteredPagedAsync(offset, pageSize, statusFilter, agentId, clientId, cursor, ct);
        var items = tasks.Select(MapToDetailResponse).ToList();

        // Compute the next cursor: the Id of the last item in this page (results are ORDER BY Id DESC)
        int? nextCursor = items.Count == pageSize && items.Count > 0
            ? items[^1].Id
            : null;

        return Ok(new PaginatedResponse<TaskDetailResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            NextCursor = nextCursor
        });
    }

    /// <summary>
    /// Search tasks by title or description.
    /// </summary>
    [HttpGet("search")]
    [ProducesResponseType(typeof(PaginatedResponse<TaskDetailResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PaginatedResponse<TaskDetailResponse>>> SearchTasks(
        [FromQuery] string q,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Ok(new PaginatedResponse<TaskDetailResponse> { Page = page, PageSize = pageSize });

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        TaskStatus? statusFilter = null;
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<TaskStatus>(status, ignoreCase: true, out var parsed))
            statusFilter = parsed;

        var offset = (page - 1) * pageSize;
        var (tasks, totalCount) = await _taskRepository.SearchAsync(q, offset, pageSize, statusFilter, ct);
        var items = tasks.Select(MapToDetailResponse).ToList();

        int? nextCursor = items.Count == pageSize && items.Count > 0 ? items[^1].Id : null;

        return Ok(new PaginatedResponse<TaskDetailResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            NextCursor = nextCursor
        });
    }

    /// <summary>
    /// Create multiple tasks in a single request. Returns the created task IDs.
    /// </summary>
    [HttpPost("batch")]
    [ProducesResponseType(typeof(BatchCreateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BatchCreateResponse>> BatchCreateTasks(
        [FromBody] List<CreateTaskRequest> requests,
        CancellationToken ct)
    {
        if (requests is null || requests.Count == 0)
            return BadRequest(ApiError.BadRequest("At least one task is required."));
        if (requests.Count > 50)
            return BadRequest(ApiError.BadRequest("Maximum 50 tasks per batch request."));

        var results = new List<BatchCreateResult>();

        foreach (var request in requests)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Description))
                {
                    results.Add(new BatchCreateResult { Success = false, Error = "Title and Description are required." });
                    continue;
                }

                if (!Enum.TryParse<TaskType>(request.TaskType, ignoreCase: true, out var taskType))
                {
                    results.Add(new BatchCreateResult { Success = false, Error = $"Invalid TaskType '{request.TaskType}'." });
                    continue;
                }

                if (request.MaxPayoutSats <= 0)
                {
                    results.Add(new BatchCreateResult { Success = false, Error = "MaxPayoutSats must be greater than zero." });
                    continue;
                }

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
                _metrics.IncrementTasksCreated();

                results.Add(new BatchCreateResult
                {
                    Success = true,
                    TaskId = id,
                    ExternalId = externalId
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Batch task creation failed for item");
                results.Add(new BatchCreateResult { Success = false, Error = ex.Message });
            }
        }

        return Ok(new BatchCreateResponse
        {
            Total = results.Count,
            Succeeded = results.Count(r => r.Success),
            Failed = results.Count(r => !r.Success),
            Results = results
        });
    }

    /// <summary>
    /// Get the status of multiple tasks by their IDs.
    /// </summary>
    [HttpPost("batch/status")]
    [ProducesResponseType(typeof(List<TaskDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<TaskDetailResponse>>> BatchGetStatus(
        [FromBody] List<int> taskIds,
        CancellationToken ct)
    {
        if (taskIds is null || taskIds.Count == 0)
            return BadRequest(ApiError.BadRequest("At least one task ID is required."));
        if (taskIds.Count > 100)
            return BadRequest(ApiError.BadRequest("Maximum 100 task IDs per request."));

        var tasks = await _taskRepository.GetByIdsAsync(taskIds, ct);
        var items = tasks.Select(MapToDetailResponse).ToList();

        return Ok(items);
    }

    /// <summary>
    /// Get a single task by ID, including its milestones.
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(TaskDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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

    /// <summary>
    /// Assign an agent to a task. Admin only.
    /// </summary>
    [HttpPost("{id:int}/assign")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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

    /// <summary>
    /// Cancel a task by setting its status to Failed.
    /// </summary>
    [HttpPost("{id:int}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CancelTask(int id, CancellationToken ct)
    {
        var task = await _taskRepository.GetByIdAsync(id, ct);
        if (task is null)
            return NotFound($"Task {id} not found.");

        await _taskRepository.UpdateStatusAsync(id, TaskStatus.Failed, ct);

        _logger.LogInformation("Task {TaskId} cancelled", id);

        return Ok(new { message = $"Task {id} cancelled." });
    }

    /// <summary>
    /// Get all subtasks for a parent task.
    /// </summary>
    [HttpGet("{id:int}/subtasks")]
    [ProducesResponseType(typeof(List<TaskDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<TaskDetailResponse>>> GetSubtasks(int id, CancellationToken ct)
    {
        var subtasks = await _taskRepository.GetSubtasksAsync(id, ct);
        var result = subtasks.Select(MapToDetailResponse).ToList();
        return Ok(result);
    }

    /// <summary>
    /// Start orchestration for a task (decomposition, agent matching, milestone creation).
    /// </summary>
    [HttpPost("{id:int}/orchestrate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> OrchestrateTask(int id, CancellationToken ct)
    {
        var task = await _taskRepository.GetByIdAsync(id, ct);
        if (task is null)
            return NotFound($"Task {id} not found.");

        // Block orchestration if task has an incomplete dependency
        if (task.DependsOnTaskId.HasValue)
        {
            var depTask = await _taskRepository.GetByIdAsync(task.DependsOnTaskId.Value, ct);
            if (depTask is not null && depTask.Status != TaskStatus.Completed)
                return BadRequest($"Task {id} depends on task {task.DependsOnTaskId.Value} which is not yet Completed (current status: {depTask.Status}).");
        }

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

    /// <summary>
    /// Assemble and return the final deliverable for a completed task.
    /// </summary>
    [HttpGet("{id:int}/deliverable")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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

    /// <summary>
    /// Retry all failed milestones for a task and its subtasks.
    /// </summary>
    [HttpPost("{id:int}/retry")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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
            var failedMilestones = milestones.Where(m => m.Status == MilestoneStatus.Failed).ToList();

            foreach (var milestone in failedMilestones)
            {
                _logger.LogInformation(
                    "Retrying failed milestone {MilestoneId} for subtask {SubtaskId} of task {TaskId}",
                    milestone.Id, subtask.Id, id);

                await _workflow.ProcessRetryAsync(milestone.Id, ct);
                retriedCount++;
            }

            // Reset the subtask status so the WorkerAgent picks it up again
            if (failedMilestones.Count > 0 && subtask.Status == TaskStatus.Failed)
            {
                await _taskRepository.UpdateStatusAsync(subtask.Id, TaskStatus.InProgress, ct);
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

    /// <summary>
    /// Enqueue a task for background orchestration.
    /// </summary>
    [HttpPost("{id:int}/enqueue")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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
    [Range(1, int.MaxValue, ErrorMessage = "AgentId must be a positive integer.")]
    public int AgentId { get; set; }
}
