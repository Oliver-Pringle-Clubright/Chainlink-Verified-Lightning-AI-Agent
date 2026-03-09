using LightningAgent.Api.DTOs;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace LightningAgent.Api.Controllers;

[ApiController]
[Route("api/agents")]
public class AgentsController : ControllerBase
{
    private readonly IAgentRepository _agentRepository;
    private readonly IAgentCapabilityRepository _capabilityRepository;
    private readonly IAgentReputationRepository _reputationRepository;
    private readonly ILogger<AgentsController> _logger;

    public AgentsController(
        IAgentRepository agentRepository,
        IAgentCapabilityRepository capabilityRepository,
        IAgentReputationRepository reputationRepository,
        ILogger<AgentsController> logger)
    {
        _agentRepository = agentRepository;
        _capabilityRepository = capabilityRepository;
        _reputationRepository = reputationRepository;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<ActionResult<RegisterAgentResponse>> RegisterAgent(
        [FromBody] RegisterAgentRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");

        var externalId = request.ExternalId ?? Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;

        var agent = new Agent
        {
            ExternalId = externalId,
            Name = request.Name,
            WalletPubkey = request.WalletPubkey,
            Status = AgentStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };

        var agentId = await _agentRepository.CreateAsync(agent, ct);

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

        return Ok(new RegisterAgentResponse
        {
            AgentId = agentId,
            ExternalId = externalId,
            Status = AgentStatus.Active.ToString()
        });
    }

    [HttpGet]
    public async Task<ActionResult<List<AgentDetailResponse>>> ListAgents(
        [FromQuery] string? status,
        CancellationToken ct)
    {
        AgentStatus? statusFilter = null;
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<AgentStatus>(status, ignoreCase: true, out var parsed))
        {
            statusFilter = parsed;
        }

        var agents = await _agentRepository.GetAllAsync(statusFilter, ct);

        var result = agents.Select(a => new AgentDetailResponse
        {
            Id = a.Id,
            ExternalId = a.ExternalId,
            Name = a.Name,
            WalletPubkey = a.WalletPubkey,
            Status = a.Status.ToString()
        }).ToList();

        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AgentDetailResponse>> GetAgent(int id, CancellationToken ct)
    {
        var agent = await _agentRepository.GetByIdAsync(id, ct);
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

        // Load reputation
        var reputation = await _reputationRepository.GetByAgentIdAsync(id, ct);
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

        // Load capabilities
        var capabilities = await _capabilityRepository.GetByAgentIdAsync(id, ct);
        response.Capabilities = capabilities.Select(c => new AgentCapabilityDto
        {
            SkillType = c.SkillType.ToString(),
            TaskTypes = c.TaskTypes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            MaxConcurrency = c.MaxConcurrency,
            PriceSatsPerUnit = c.PriceSatsPerUnit
        }).ToList();

        return Ok(response);
    }

    [HttpPut("{id:int}/capabilities")]
    public async Task<IActionResult> UpdateCapabilities(
        int id,
        [FromBody] List<AgentCapabilityDto> capabilities,
        CancellationToken ct)
    {
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

        _logger.LogInformation("Updated capabilities for agent {AgentId}", id);

        return Ok(new { message = $"Capabilities updated for agent {id}." });
    }

    [HttpGet("{id:int}/reputation")]
    public async Task<ActionResult<ReputationDto>> GetReputation(int id, CancellationToken ct)
    {
        var reputation = await _reputationRepository.GetByAgentIdAsync(id, ct);
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

    [HttpPost("{id:int}/suspend")]
    public async Task<IActionResult> SuspendAgent(int id, CancellationToken ct)
    {
        var agent = await _agentRepository.GetByIdAsync(id, ct);
        if (agent is null)
            return NotFound($"Agent {id} not found.");

        await _agentRepository.UpdateStatusAsync(id, AgentStatus.Suspended, ct);

        _logger.LogInformation("Agent {AgentId} suspended", id);

        return Ok(new { message = $"Agent {id} suspended." });
    }
}
