using System.Text.Json;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using LightningAgent.Core.Models.Acp;
using Microsoft.AspNetCore.Mvc;
using TaskStatus = LightningAgent.Core.Enums.TaskStatus;

namespace LightningAgent.Api.Controllers;

[ApiController]
[Route("api/acp")]
public class AcpController : ControllerBase
{
    private readonly ITaskOrchestrator _taskOrchestrator;
    private readonly INaturalLanguageTaskParser _nlParser;
    private readonly IAgentMatcher _agentMatcher;
    private readonly ITaskRepository _taskRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly IAgentCapabilityRepository _capabilityRepository;
    private readonly ILogger<AcpController> _logger;

    public AcpController(
        ITaskOrchestrator taskOrchestrator,
        INaturalLanguageTaskParser nlParser,
        IAgentMatcher agentMatcher,
        ITaskRepository taskRepository,
        IAgentRepository agentRepository,
        IAgentCapabilityRepository capabilityRepository,
        ILogger<AcpController> logger)
    {
        _taskOrchestrator = taskOrchestrator;
        _nlParser = nlParser;
        _agentMatcher = agentMatcher;
        _taskRepository = taskRepository;
        _agentRepository = agentRepository;
        _capabilityRepository = capabilityRepository;
        _logger = logger;
    }

    /// <summary>
    /// ACP Service Discovery: lists available agent services, optionally filtered by task type.
    /// </summary>
    [HttpGet("services")]
    public async Task<ActionResult<List<AcpServiceDescriptor>>> GetServices(
        [FromQuery] string? taskType,
        CancellationToken ct)
    {
        IReadOnlyList<Agent> agents;

        if (!string.IsNullOrWhiteSpace(taskType))
        {
            // Find agents whose capabilities include the requested task type
            var allAgents = await _agentRepository.GetAllAsync(AgentStatus.Active, ct);
            var matched = new List<Agent>();

            foreach (var agent in allAgents)
            {
                var capabilities = await _capabilityRepository.GetByAgentIdAsync(agent.Id, ct);
                if (capabilities.Any(c => c.TaskTypes
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Contains(taskType, StringComparer.OrdinalIgnoreCase)))
                {
                    matched.Add(agent);
                }
            }

            agents = matched;
        }
        else
        {
            agents = await _agentRepository.GetAllAsync(AgentStatus.Active, ct);
        }

        var descriptors = new List<AcpServiceDescriptor>();

        foreach (var agent in agents)
        {
            var capabilities = await _capabilityRepository.GetByAgentIdAsync(agent.Id, ct);

            var taskTypes = capabilities
                .SelectMany(c => c.TaskTypes
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct()
                .ToList();

            var minPrice = capabilities.Any() ? capabilities.Min(c => c.PriceSatsPerUnit) : 0;
            var maxPrice = capabilities.Any() ? capabilities.Max(c => c.PriceSatsPerUnit) : 0;

            descriptors.Add(new AcpServiceDescriptor
            {
                ServiceId = $"svc-{agent.ExternalId}",
                AgentId = agent.ExternalId,
                Name = agent.Name,
                Description = $"Agent {agent.Name} with capabilities: {string.Join(", ", capabilities.Select(c => c.SkillType))}",
                SupportedTaskTypes = taskTypes,
                PriceRange = new AcpPriceRange
                {
                    MinSats = minPrice,
                    MaxSats = maxPrice
                },
                Endpoint = $"/api/acp/tasks",
                IsAvailable = agent.Status == AgentStatus.Active
            });
        }

        return Ok(descriptors);
    }

    /// <summary>
    /// ACP-compatible task posting: accepts an AcpTaskSpec and creates a TaskItem.
    /// </summary>
    [HttpPost("tasks")]
    public async Task<ActionResult> PostTask(
        [FromBody] AcpTaskSpec spec,
        [FromQuery] bool orchestrate = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(spec.Title))
            return BadRequest("Title is required.");
        if (string.IsNullOrWhiteSpace(spec.Description))
            return BadRequest("Description is required.");

        var externalId = string.IsNullOrWhiteSpace(spec.TaskId)
            ? Guid.NewGuid().ToString("N")
            : spec.TaskId;

        if (!Enum.TryParse<TaskType>(spec.TaskType, ignoreCase: true, out var taskType))
            taskType = TaskType.Code; // default fallback

        var now = DateTime.UtcNow;
        var task = new TaskItem
        {
            ExternalId = externalId,
            ClientId = "acp",
            Title = spec.Title,
            Description = spec.Description,
            TaskType = taskType,
            Status = TaskStatus.Pending,
            VerificationCriteria = spec.VerificationRequirements,
            MaxPayoutSats = spec.Budget.MaxSats,
            AcpSpec = JsonSerializer.Serialize(spec),
            Priority = 0,
            CreatedAt = now,
            UpdatedAt = now
        };

        var id = await _taskRepository.CreateAsync(task, ct);
        task.Id = id;

        _logger.LogInformation(
            "ACP task created {TaskId} (ext: {ExternalId}) type={TaskType}",
            id, externalId, taskType);

        if (orchestrate)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _taskOrchestrator.OrchestrateTaskAsync(task, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background orchestration failed for task {TaskId}", id);
                }
            }, CancellationToken.None);
        }

        return Ok(new
        {
            taskId = id,
            externalId,
            status = TaskStatus.Pending.ToString()
        });
    }

    /// <summary>
    /// ACP price negotiation: simple midpoint-based negotiation.
    /// </summary>
    [HttpPost("negotiate")]
    public ActionResult Negotiate([FromBody] NegotiateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TaskId))
            return BadRequest("TaskId is required.");
        if (request.RequesterBudgetSats <= 0)
            return BadRequest("RequesterBudgetSats must be greater than zero.");
        if (request.WorkerAskingSats <= 0)
            return BadRequest("WorkerAskingSats must be greater than zero.");

        var midpoint = (request.RequesterBudgetSats + request.WorkerAskingSats) / 2;

        // Accept if the midpoint is within the requester's budget
        bool accepted = midpoint <= request.RequesterBudgetSats;

        _logger.LogInformation(
            "ACP negotiate task={TaskId}: requester={RequesterBudget}, worker={WorkerAsking}, proposed={Midpoint}, accepted={Accepted}",
            request.TaskId, request.RequesterBudgetSats, request.WorkerAskingSats, midpoint, accepted);

        return Ok(new
        {
            proposedPriceSats = midpoint,
            accepted
        });
    }

    /// <summary>
    /// ACP completion notification: marks a task as completed.
    /// </summary>
    [HttpPost("complete")]
    public async Task<ActionResult> Complete(
        [FromBody] CompleteRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.TaskId))
            return BadRequest("TaskId is required.");

        // Try parsing as int first, then as external ID
        TaskItem? task = null;
        if (int.TryParse(request.TaskId, out var intId))
        {
            task = await _taskRepository.GetByIdAsync(intId, ct);
        }

        task ??= await _taskRepository.GetByExternalIdAsync(request.TaskId, ct);

        if (task is null)
            return NotFound($"Task '{request.TaskId}' not found.");

        task.Status = TaskStatus.Completed;
        task.CompletedAt = DateTime.UtcNow;
        task.UpdatedAt = DateTime.UtcNow;
        await _taskRepository.UpdateAsync(task, ct);

        _logger.LogInformation("ACP task {TaskId} marked as completed", task.Id);

        return Ok(new { message = $"Task {task.Id} completed." });
    }
}

public class NegotiateRequest
{
    public string TaskId { get; set; } = string.Empty;
    public long RequesterBudgetSats { get; set; }
    public long WorkerAskingSats { get; set; }
}

public class CompleteRequest
{
    public string TaskId { get; set; } = string.Empty;
    public string? Result { get; set; }
}
