using FluentAssertions;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using LightningAgent.Engine;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LightningAgent.Tests.Unit;

public class AgentMatcherTests
{
    private readonly IAgentRepository _agentRepo;
    private readonly IAgentCapabilityRepository _capabilityRepo;
    private readonly IAgentReputationRepository _reputationRepo;
    private readonly ILogger<AgentMatcher> _logger;
    private readonly AgentMatcher _sut;

    public AgentMatcherTests()
    {
        _agentRepo = Substitute.For<IAgentRepository>();
        _capabilityRepo = Substitute.For<IAgentCapabilityRepository>();
        _reputationRepo = Substitute.For<IAgentReputationRepository>();
        _logger = Substitute.For<ILogger<AgentMatcher>>();
        _sut = new AgentMatcher(_agentRepo, _capabilityRepo, _reputationRepo, _logger);
    }

    [Fact]
    public async Task Test_FindBestAgent_ReturnsEmpty_WhenNoAgents()
    {
        // Arrange
        _agentRepo.GetAllAsync(AgentStatus.Active).Returns(new List<Agent>());

        var task = CreateTask(TaskType.Code, maxPayoutSats: 10000);

        // Act
        var result = await _sut.FindBestAgentAsync(task);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Test_FindBestAgent_FiltersBy_TaskType()
    {
        // Arrange: agent1 has CodeGeneration, agent2 has TextWriting
        var agent1 = CreateAgent(1, "Agent1");
        var agent2 = CreateAgent(2, "Agent2");
        _agentRepo.GetAllAsync(AgentStatus.Active).Returns(new List<Agent> { agent1, agent2 });

        _capabilityRepo.GetByAgentIdAsync(1).Returns(new List<AgentCapability>
        {
            new() { AgentId = 1, SkillType = SkillType.CodeGeneration, PriceSatsPerUnit = 1000, MaxConcurrency = 1 }
        });
        _capabilityRepo.GetByAgentIdAsync(2).Returns(new List<AgentCapability>
        {
            new() { AgentId = 2, SkillType = SkillType.TextWriting, PriceSatsPerUnit = 500, MaxConcurrency = 1 }
        });

        _reputationRepo.GetByAgentIdAsync(Arg.Any<int>()).Returns((AgentReputation?)null);

        var task = CreateTask(TaskType.Code, maxPayoutSats: 10000);

        // Act
        var result = await _sut.FindBestAgentAsync(task);

        // Assert: only agent1 has CodeGeneration skill
        result.Should().HaveCount(1);
        result[0].Agent.Id.Should().Be(1);
    }

    [Fact]
    public async Task Test_FindBestAgent_RankedBy_ReputationScore()
    {
        // Arrange: two agents, one with higher reputation
        var agent1 = CreateAgent(1, "LowRep");
        var agent2 = CreateAgent(2, "HighRep");
        _agentRepo.GetAllAsync(AgentStatus.Active).Returns(new List<Agent> { agent1, agent2 });

        var codeCapability = new AgentCapability
        {
            SkillType = SkillType.CodeGeneration,
            PriceSatsPerUnit = 5000,
            MaxConcurrency = 1
        };

        _capabilityRepo.GetByAgentIdAsync(1).Returns(new List<AgentCapability>
        {
            new() { AgentId = 1, SkillType = SkillType.CodeGeneration, PriceSatsPerUnit = 5000, MaxConcurrency = 1 }
        });
        _capabilityRepo.GetByAgentIdAsync(2).Returns(new List<AgentCapability>
        {
            new() { AgentId = 2, SkillType = SkillType.CodeGeneration, PriceSatsPerUnit = 5000, MaxConcurrency = 1 }
        });

        _reputationRepo.GetByAgentIdAsync(1).Returns(new AgentReputation { AgentId = 1, ReputationScore = 0.3 });
        _reputationRepo.GetByAgentIdAsync(2).Returns(new AgentReputation { AgentId = 2, ReputationScore = 0.9 });

        var task = CreateTask(TaskType.Code, maxPayoutSats: 10000);

        // Act
        var result = await _sut.FindBestAgentAsync(task);

        // Assert: agent2 (higher reputation) should be first
        result.Should().HaveCount(2);
        result[0].Agent.Id.Should().Be(2);
        result[0].MatchScore.Should().BeGreaterThan(result[1].MatchScore);
    }

    [Fact]
    public async Task Test_FindBestAgent_PriceWeight_FavorsLowerPrice()
    {
        // Arrange: two agents with same reputation but different prices
        var agent1 = CreateAgent(1, "Expensive");
        var agent2 = CreateAgent(2, "Cheap");
        _agentRepo.GetAllAsync(AgentStatus.Active).Returns(new List<Agent> { agent1, agent2 });

        _capabilityRepo.GetByAgentIdAsync(1).Returns(new List<AgentCapability>
        {
            new() { AgentId = 1, SkillType = SkillType.CodeGeneration, PriceSatsPerUnit = 9000, MaxConcurrency = 1 }
        });
        _capabilityRepo.GetByAgentIdAsync(2).Returns(new List<AgentCapability>
        {
            new() { AgentId = 2, SkillType = SkillType.CodeGeneration, PriceSatsPerUnit = 1000, MaxConcurrency = 1 }
        });

        // Same reputation
        _reputationRepo.GetByAgentIdAsync(1).Returns(new AgentReputation { AgentId = 1, ReputationScore = 0.5 });
        _reputationRepo.GetByAgentIdAsync(2).Returns(new AgentReputation { AgentId = 2, ReputationScore = 0.5 });

        var task = CreateTask(TaskType.Code, maxPayoutSats: 10000);

        // Act
        var result = await _sut.FindBestAgentAsync(task);

        // Assert: cheaper agent should rank higher
        result.Should().HaveCount(2);
        result[0].Agent.Id.Should().Be(2);
        result[0].MatchScore.Should().BeGreaterThan(result[1].MatchScore);
    }

    [Fact]
    public async Task Test_FindBestAgent_SkipsInactiveAgents()
    {
        // Arrange: only active agents are returned by repo
        var activeAgent = CreateAgent(1, "ActiveAgent");
        _agentRepo.GetAllAsync(AgentStatus.Active).Returns(new List<Agent> { activeAgent });

        _capabilityRepo.GetByAgentIdAsync(1).Returns(new List<AgentCapability>
        {
            new() { AgentId = 1, SkillType = SkillType.CodeGeneration, PriceSatsPerUnit = 1000, MaxConcurrency = 1 }
        });
        _reputationRepo.GetByAgentIdAsync(1).Returns((AgentReputation?)null);

        var task = CreateTask(TaskType.Code, maxPayoutSats: 10000);

        // Act
        var result = await _sut.FindBestAgentAsync(task);

        // Assert: only the active agent appears
        result.Should().HaveCount(1);
        result[0].Agent.Id.Should().Be(1);

        // Verify that the repo was only called with Active status
        await _agentRepo.Received(1).GetAllAsync(AgentStatus.Active);
    }

    private static Agent CreateAgent(int id, string name) => new()
    {
        Id = id,
        ExternalId = $"ext-{id}",
        Name = name,
        Status = AgentStatus.Active,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static TaskItem CreateTask(TaskType taskType, long maxPayoutSats) => new()
    {
        Id = 1,
        ExternalId = "task-1",
        ClientId = "client-1",
        Title = "Test Task",
        Description = "A test task",
        TaskType = taskType,
        Status = Core.Enums.TaskStatus.Pending,
        MaxPayoutSats = maxPayoutSats,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
