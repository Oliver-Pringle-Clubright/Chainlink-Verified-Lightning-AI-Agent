using LightningAgent.Api.DTOs;
using LightningAgent.Api.Helpers;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using LightningAgent.Data;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using TaskStatus = LightningAgent.Core.Enums.TaskStatus;

namespace LightningAgent.Api.Controllers;

/// <summary>
/// Manages agent registration, capabilities, reputation, and lifecycle.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/agents")]
[Route("api/v{version:apiVersion}/agents")]
[Produces("application/json")]
public class AgentsController : ControllerBase
{
    private readonly IAgentRepository _agentRepository;
    private readonly IAgentCapabilityRepository _capabilityRepository;
    private readonly IAgentReputationRepository _reputationRepository;
    private readonly ITaskRepository _taskRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ICachedDataService _cachedData;
    private readonly ILogger<AgentsController> _logger;

    public AgentsController(
        IAgentRepository agentRepository,
        IAgentCapabilityRepository capabilityRepository,
        IAgentReputationRepository reputationRepository,
        ITaskRepository taskRepository,
        IEventPublisher eventPublisher,
        ICachedDataService cachedData,
        ILogger<AgentsController> logger)
    {
        _agentRepository = agentRepository;
        _capabilityRepository = capabilityRepository;
        _reputationRepository = reputationRepository;
        _taskRepository = taskRepository;
        _eventPublisher = eventPublisher;
        _cachedData = cachedData;
        _logger = logger;
    }

    /// <summary>
    /// Register a new agent with optional capabilities.
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(RegisterAgentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RegisterAgentResponse>> RegisterAgent(
        [FromBody] RegisterAgentRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");

        // Validate capabilities if provided
        if (request.Capabilities is { Count: > 0 })
        {
            for (int i = 0; i < request.Capabilities.Count; i++)
            {
                var cap = request.Capabilities[i];
                if (!Enum.TryParse<SkillType>(cap.SkillType, ignoreCase: true, out _))
                    return BadRequest($"Capability [{i}]: Invalid SkillType '{cap.SkillType}'. Valid values: {string.Join(", ", Enum.GetNames<SkillType>())}");
                if (cap.PriceSatsPerUnit <= 0)
                    return BadRequest($"Capability [{i}]: PriceSatsPerUnit must be greater than zero.");
            }
        }

        var externalId = request.ExternalId ?? Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;

        // Generate a unique API key for this agent
        var plaintextApiKey = Guid.NewGuid().ToString("N");
        var apiKeyHash = ApiKeyHasher.Hash(plaintextApiKey);

        var agent = new Agent
        {
            ExternalId = externalId,
            Name = request.Name,
            WalletPubkey = request.WalletPubkey,
            WebhookUrl = request.WebhookUrl,
            ApiKeyHash = apiKeyHash,
            Status = AgentStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };

        int agentId;
        try
        {
            agentId = await _agentRepository.CreateAsync(agent, ct);
        }
        catch (SqliteException ex) when (SqliteExceptionHandler.IsUniqueConstraintViolation(ex))
        {
            return Conflict($"An agent with ExternalId '{externalId}' already exists.");
        }
        catch (SqliteException ex) when (SqliteExceptionHandler.IsForeignKeyViolation(ex))
        {
            return BadRequest("Invalid foreign key reference in agent data.");
        }

        // Create capabilities if provided
        if (request.Capabilities is { Count: > 0 })
        {
            foreach (var capDto in request.Capabilities)
            {
                if (!Enum.TryParse<SkillType>(capDto.SkillType, ignoreCase: true, out var skillType))
                    continue;

                var capability = new AgentCapability
                {
                    AgentId = agentId,
                    SkillType = skillType,
                    TaskTypes = string.Join(",", capDto.TaskTypes),
                    MaxConcurrency = capDto.MaxConcurrency ?? 1,
                    PriceSatsPerUnit = capDto.PriceSatsPerUnit,
                    CreatedAt = now
                };

                await _capabilityRepository.CreateAsync(capability, ct);
            }
        }

        // Create initial reputation record
        var reputation = new AgentReputation
        {
            AgentId = agentId,
            TotalTasks = 0,
            CompletedTasks = 0,
            VerificationPasses = 0,
            VerificationFails = 0,
            DisputeCount = 0,
            AvgResponseTimeSec = 0,
            ReputationScore = 0.5,
            LastUpdated = now
        };

        await _reputationRepository.CreateAsync(reputation, ct);

        _logger.LogInformation("Registered agent {AgentId} (ext: {ExternalId})", agentId, externalId);

        await _eventPublisher.PublishAgentRegisteredAsync(agentId, request.Name, ct);

        return Ok(new RegisterAgentResponse
        {
            AgentId = agentId,
            ExternalId = externalId,
            Status = AgentStatus.Active.ToString(),
            ApiKey = plaintextApiKey
        });
    }

    /// <summary>
    /// List agents with optional status filter and pagination.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<AgentDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PaginatedResponse<AgentDetailResponse>>> ListAgents(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        AgentStatus? statusFilter = null;
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<AgentStatus>(status, ignoreCase: true, out var parsed))
        {
            statusFilter = parsed;
        }

        var offset = (page - 1) * pageSize;
        var totalCount = await _agentRepository.GetCountAsync(statusFilter, ct);
        var agents = await _agentRepository.GetPagedAsync(offset, pageSize, statusFilter, ct);

        var result = agents.Select(a => new AgentDetailResponse
        {
            Id = a.Id,
            ExternalId = a.ExternalId,
            Name = a.Name,
            WalletPubkey = a.WalletPubkey,
            Status = a.Status.ToString()
        }).ToList();

        return Ok(new PaginatedResponse<AgentDetailResponse>
        {
            Items = result,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    /// <summary>
    /// Get a single agent by ID, including reputation and capabilities (cached).
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(AgentDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<AgentDetailResponse>> GetAgent(int id, CancellationToken ct)
    {
        if (!AuthorizationHelper.CanAccessAgent(HttpContext, id))
            return Forbid();

        var agent = await _cachedData.GetAgentAsync(id, ct);
        if (agent is null)
            return NotFound($"Agent {id} not found.");

        var response = new AgentDetailResponse
        {
            Id = agent.Id,
            ExternalId = agent.ExternalId,
            Name = agent.Name,
            WalletPubkey = agent.WalletPubkey,
            Status = agent.Status.ToString()
        };

        // Load reputation (cached)
        var reputation = await _cachedData.GetAgentReputationAsync(id, ct);
        if (reputation is not null)
        {
            response.Reputation = new ReputationDto
            {
                TotalTasks = reputation.TotalTasks,
                CompletedTasks = reputation.CompletedTasks,
                VerificationPasses = reputation.VerificationPasses,
                VerificationFails = reputation.VerificationFails,
                ReputationScore = reputation.ReputationScore
            };
        }

        // Load capabilities (cached)
        var capabilities = await _cachedData.GetAgentCapabilitiesAsync(id, ct);
        response.Capabilities = capabilities.Select(c => new AgentCapabilityDto
        {
            SkillType = c.SkillType.ToString(),
            TaskTypes = c.TaskTypes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            MaxConcurrency = c.MaxConcurrency,
            PriceSatsPerUnit = c.PriceSatsPerUnit
        }).ToList();

        return Ok(response);
    }

    /// <summary>
    /// Replace all capabilities for an agent.
    /// </summary>
    [HttpPut("{id:int}/capabilities")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateCapabilities(
        int id,
        [FromBody] List<AgentCapabilityDto> capabilities,
        CancellationToken ct)
    {
        if (!AuthorizationHelper.CanAccessAgent(HttpContext, id))
            return Forbid();

        var agent = await _agentRepository.GetByIdAsync(id, ct);
        if (agent is null)
            return NotFound($"Agent {id} not found.");

        // Remove existing capabilities and re-create
        await _capabilityRepository.DeleteByAgentIdAsync(id, ct);

        var now = DateTime.UtcNow;
        foreach (var capDto in capabilities)
        {
            if (!Enum.TryParse<SkillType>(capDto.SkillType, ignoreCase: true, out var skillType))
                continue;

            var capability = new AgentCapability
            {
                AgentId = id,
                SkillType = skillType,
                TaskTypes = string.Join(",", capDto.TaskTypes),
                MaxConcurrency = capDto.MaxConcurrency ?? 1,
                PriceSatsPerUnit = capDto.PriceSatsPerUnit,
                CreatedAt = now
            };

            await _capabilityRepository.CreateAsync(capability, ct);
        }

        // Invalidate cached data for this agent
        _cachedData.InvalidateAgent(id);

        _logger.LogInformation("Updated capabilities for agent {AgentId}", id);

        return Ok(new { message = $"Capabilities updated for agent {id}." });
    }

    /// <summary>
    /// Get the reputation record for an agent (cached).
    /// </summary>
    [HttpGet("{id:int}/reputation")]
    [ProducesResponseType(typeof(ReputationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ReputationDto>> GetReputation(int id, CancellationToken ct)
    {
        var reputation = await _cachedData.GetAgentReputationAsync(id, ct);
        if (reputation is null)
            return NotFound($"Reputation for agent {id} not found.");

        return Ok(new ReputationDto
        {
            TotalTasks = reputation.TotalTasks,
            CompletedTasks = reputation.CompletedTasks,
            VerificationPasses = reputation.VerificationPasses,
            VerificationFails = reputation.VerificationFails,
            ReputationScore = reputation.ReputationScore
        });
    }

    /// <summary>
    /// List active (assigned or in-progress) tasks for an agent.
    /// </summary>
    [HttpGet("{id:int}/assigned-tasks")]
    [ProducesResponseType(typeof(List<TaskDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<TaskDetailResponse>>> GetAssignedTasks(int id, CancellationToken ct)
    {
        if (!AuthorizationHelper.CanAccessAgent(HttpContext, id))
            return Forbid();

        var agent = await _cachedData.GetAgentAsync(id, ct);
        if (agent is null)
            return NotFound($"Agent {id} not found.");

        var allTasks = await _taskRepository.GetByAssignedAgentAsync(id, ct);
        var activeTasks = allTasks
            .Where(t => t.Status is TaskStatus.Assigned or TaskStatus.InProgress)
            .ToList();

        var result = activeTasks.Select(t => new TaskDetailResponse
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
        }).ToList();

        return Ok(result);
    }

    /// <summary>
    /// Suspend an agent. Admin only.
    /// </summary>
    [HttpPost("{id:int}/suspend")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SuspendAgent(int id, CancellationToken ct)
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext))
            return Forbid();

        var agent = await _agentRepository.GetByIdAsync(id, ct);
        if (agent is null)
            return NotFound($"Agent {id} not found.");

        await _agentRepository.UpdateStatusAsync(id, AgentStatus.Suspended, ct);

        // Invalidate cached data for this agent
        _cachedData.InvalidateAgent(id);

        _logger.LogInformation("Agent {AgentId} suspended", id);

        return Ok(new { message = $"Agent {id} suspended." });
    }
}
