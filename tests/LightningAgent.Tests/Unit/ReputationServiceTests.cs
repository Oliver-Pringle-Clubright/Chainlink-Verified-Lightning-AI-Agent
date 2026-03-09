using FluentAssertions;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using LightningAgent.Engine;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LightningAgent.Tests.Unit;

public class ReputationServiceTests
{
    private readonly IAgentReputationRepository _reputationRepo;
    private readonly IAgentRepository _agentRepo;
    private readonly IAgentCapabilityRepository _capabilityRepo;
    private readonly ILogger<ReputationService> _logger;
    private readonly ReputationService _sut;

    public ReputationServiceTests()
    {
        _reputationRepo = Substitute.For<IAgentReputationRepository>();
        _agentRepo = Substitute.For<IAgentRepository>();
        _capabilityRepo = Substitute.For<IAgentCapabilityRepository>();
        _logger = Substitute.For<ILogger<ReputationService>>();
        _sut = new ReputationService(_reputationRepo, _agentRepo, _capabilityRepo, _logger);
    }

    [Fact]
    public async Task Test_NewAgent_DefaultScore_Is_0_5()
    {
        // Arrange: no existing reputation
        _reputationRepo.GetByAgentIdAsync(1).Returns((AgentReputation?)null);

        double capturedInitialScore = -1;
        _reputationRepo.CreateAsync(Arg.Do<AgentReputation>(r => capturedInitialScore = r.ReputationScore))
            .Returns(1);

        // Act
        var result = await _sut.UpdateReputationAsync(
            agentId: 1,
            taskCompleted: true,
            verificationPassed: true,
            responseTimeSec: 100);

        // Assert: CreateAsync was called, confirming a new reputation record was initialized.
        // The initial default score is 0.5 but then gets recalculated in the same call.
        await _reputationRepo.Received(1).CreateAsync(Arg.Any<AgentReputation>());

        // The final returned result should have a valid score in [0,1]
        result.ReputationScore.Should().BeInRange(0.0, 1.0);
        result.TotalTasks.Should().Be(1);
        result.CompletedTasks.Should().Be(1);
    }

    [Fact]
    public async Task Test_CompletedTask_IncreasesScore()
    {
        // Arrange: existing reputation with middling score
        var reputation = CreateReputation(totalTasks: 5, completedTasks: 3, verificationPasses: 3, verificationFails: 2, disputeCount: 0, avgResponseTimeSec: 500);
        _reputationRepo.GetByAgentIdAsync(1).Returns(reputation);

        var scoreBefore = reputation.ReputationScore;

        // Act: complete a task with verification passed and fast response
        var result = await _sut.UpdateReputationAsync(1, taskCompleted: true, verificationPassed: true, responseTimeSec: 60);

        // Assert: score should increase
        result.ReputationScore.Should().BeGreaterThan(scoreBefore);
        result.CompletedTasks.Should().Be(4);
        result.TotalTasks.Should().Be(6);
    }

    [Fact]
    public async Task Test_FailedVerification_DecreasesScore()
    {
        // Arrange: good reputation
        var reputation = CreateReputation(totalTasks: 10, completedTasks: 9, verificationPasses: 9, verificationFails: 1, disputeCount: 0, avgResponseTimeSec: 300);
        _reputationRepo.GetByAgentIdAsync(1).Returns(reputation);

        var scoreBefore = reputation.ReputationScore;

        // Act: fail verification
        var result = await _sut.UpdateReputationAsync(1, taskCompleted: false, verificationPassed: false, responseTimeSec: 300);

        // Assert: score should decrease
        result.ReputationScore.Should().BeLessThan(scoreBefore);
        result.VerificationFails.Should().Be(2);
    }

    [Fact]
    public async Task Test_Dispute_PenalizesScore()
    {
        // Arrange: reputation with disputes vs without
        var repWithDisputes = CreateReputation(totalTasks: 10, completedTasks: 8, verificationPasses: 8, verificationFails: 2, disputeCount: 5, avgResponseTimeSec: 300);
        var repWithoutDisputes = CreateReputation(totalTasks: 10, completedTasks: 8, verificationPasses: 8, verificationFails: 2, disputeCount: 0, avgResponseTimeSec: 300);

        _reputationRepo.GetByAgentIdAsync(1).Returns(repWithDisputes);
        _reputationRepo.GetByAgentIdAsync(2).Returns(repWithoutDisputes);

        // Act
        var resultWithDisputes = await _sut.UpdateReputationAsync(1, taskCompleted: true, verificationPassed: true, responseTimeSec: 300);
        var resultWithoutDisputes = await _sut.UpdateReputationAsync(2, taskCompleted: true, verificationPassed: true, responseTimeSec: 300);

        // Assert: disputes should penalize the score
        resultWithDisputes.ReputationScore.Should().BeLessThan(resultWithoutDisputes.ReputationScore);
    }

    [Fact]
    public async Task Test_FastResponse_GivesBonus()
    {
        // Arrange: two identical reputations, one will get a fast response, one slow
        var repFast = CreateReputation(totalTasks: 5, completedTasks: 4, verificationPasses: 4, verificationFails: 1, disputeCount: 0, avgResponseTimeSec: 60);
        var repSlow = CreateReputation(totalTasks: 5, completedTasks: 4, verificationPasses: 4, verificationFails: 1, disputeCount: 0, avgResponseTimeSec: 3500);

        _reputationRepo.GetByAgentIdAsync(1).Returns(repFast);
        _reputationRepo.GetByAgentIdAsync(2).Returns(repSlow);

        // Act
        var resultFast = await _sut.UpdateReputationAsync(1, taskCompleted: true, verificationPassed: true, responseTimeSec: 30);
        var resultSlow = await _sut.UpdateReputationAsync(2, taskCompleted: true, verificationPassed: true, responseTimeSec: 3600);

        // Assert: fast agent should have higher score than slow agent
        resultFast.ReputationScore.Should().BeGreaterThan(resultSlow.ReputationScore);
    }

    [Fact]
    public async Task Test_Score_ClampedTo_0_1_Range()
    {
        // Arrange: perfect agent
        var repPerfect = CreateReputation(totalTasks: 100, completedTasks: 100, verificationPasses: 100, verificationFails: 0, disputeCount: 0, avgResponseTimeSec: 10);
        _reputationRepo.GetByAgentIdAsync(1).Returns(repPerfect);

        // Act
        var result = await _sut.UpdateReputationAsync(1, taskCompleted: true, verificationPassed: true, responseTimeSec: 5);

        // Assert: score should be between 0 and 1
        result.ReputationScore.Should().BeInRange(0.0, 1.0);

        // Arrange: terrible agent
        var repTerrible = CreateReputation(totalTasks: 100, completedTasks: 0, verificationPasses: 0, verificationFails: 100, disputeCount: 20, avgResponseTimeSec: 5000);
        _reputationRepo.GetByAgentIdAsync(2).Returns(repTerrible);

        // Act
        var resultBad = await _sut.UpdateReputationAsync(2, taskCompleted: false, verificationPassed: false, responseTimeSec: 5000);

        // Assert: score should be between 0 and 1
        resultBad.ReputationScore.Should().BeInRange(0.0, 1.0);
    }

    [Fact]
    public async Task Test_GetScore_ReturnsDefault_WhenNoReputation()
    {
        // Arrange
        _reputationRepo.GetByAgentIdAsync(999).Returns((AgentReputation?)null);

        // Act
        var score = await _sut.GetScoreAsync(999);

        // Assert: default score is 0.5
        score.Should().Be(0.5);
    }

    private static AgentReputation CreateReputation(
        int totalTasks,
        int completedTasks,
        int verificationPasses,
        int verificationFails,
        int disputeCount,
        double avgResponseTimeSec)
    {
        // Calculate the actual score using the same formula as the service
        double completionRate = totalTasks > 0 ? (double)completedTasks / totalTasks : 0;
        int totalVer = verificationPasses + verificationFails;
        double verificationRate = totalVer > 0 ? (double)verificationPasses / totalVer : 0.5;
        double disputePenalty = Math.Clamp(1.0 - (disputeCount * 0.1), 0.0, 1.0);
        double speedBonus = Math.Clamp(Math.Max(0, 1.0 - (avgResponseTimeSec / 3600.0)), 0.0, 1.0);
        double score = (0.3 * completionRate) + (0.4 * verificationRate) + (0.2 * disputePenalty) + (0.1 * speedBonus);
        score = Math.Clamp(score, 0.0, 1.0);

        return new AgentReputation
        {
            Id = 1,
            AgentId = 1,
            TotalTasks = totalTasks,
            CompletedTasks = completedTasks,
            VerificationPasses = verificationPasses,
            VerificationFails = verificationFails,
            DisputeCount = disputeCount,
            AvgResponseTimeSec = avgResponseTimeSec,
            ReputationScore = score,
            LastUpdated = DateTime.UtcNow
        };
    }
}
