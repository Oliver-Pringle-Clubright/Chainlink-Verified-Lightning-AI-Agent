using FluentAssertions;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using LightningAgent.Verification;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LightningAgent.Tests.Unit;

public class VerificationPipelineTests
{
    private readonly ILogger<VerificationPipeline> _logger;

    public VerificationPipelineTests()
    {
        _logger = Substitute.For<ILogger<VerificationPipeline>>();
    }

    [Fact]
    public async Task Test_Pipeline_RunsAll_ApplicableStrategies()
    {
        // Arrange: two strategies that both handle Code
        var strategy1 = Substitute.For<IVerificationStrategy>();
        strategy1.CanHandle(TaskType.Code).Returns(true);
        strategy1.StrategyType.Returns(VerificationStrategyType.CodeCompile);
        strategy1.VerifyAsync(Arg.Any<Milestone>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new VerificationResult(0.9, true, "Passed", VerificationStrategyType.CodeCompile));

        var strategy2 = Substitute.For<IVerificationStrategy>();
        strategy2.CanHandle(TaskType.Code).Returns(true);
        strategy2.StrategyType.Returns(VerificationStrategyType.AiJudge);
        strategy2.VerifyAsync(Arg.Any<Milestone>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new VerificationResult(0.85, true, "Passed", VerificationStrategyType.AiJudge));

        var pipeline = new VerificationPipeline(new[] { strategy1, strategy2 }, _logger);

        var milestone = CreateMilestone("""{"taskType": "Code"}""");

        // Act
        var results = await pipeline.RunVerificationAsync(milestone, new byte[] { 1, 2, 3 });

        // Assert: both strategies ran
        results.Should().HaveCount(2);
        await strategy1.Received(1).VerifyAsync(Arg.Any<Milestone>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
        await strategy2.Received(1).VerifyAsync(Arg.Any<Milestone>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Test_Pipeline_SkipsStrategies_ThatCantHandle()
    {
        // Arrange: strategy1 handles Code, strategy2 handles Text only
        var strategy1 = Substitute.For<IVerificationStrategy>();
        strategy1.CanHandle(TaskType.Code).Returns(true);
        strategy1.StrategyType.Returns(VerificationStrategyType.CodeCompile);
        strategy1.VerifyAsync(Arg.Any<Milestone>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new VerificationResult(0.9, true, "Passed", VerificationStrategyType.CodeCompile));

        var strategy2 = Substitute.For<IVerificationStrategy>();
        strategy2.CanHandle(TaskType.Code).Returns(false);
        strategy2.StrategyType.Returns(VerificationStrategyType.TextSimilarity);

        var pipeline = new VerificationPipeline(new[] { strategy1, strategy2 }, _logger);

        var milestone = CreateMilestone("""{"taskType": "Code"}""");

        // Act
        var results = await pipeline.RunVerificationAsync(milestone, new byte[] { 1, 2, 3 });

        // Assert: only strategy1 ran
        results.Should().HaveCount(1);
        results[0].StrategyType.Should().Be(VerificationStrategyType.CodeCompile);
        await strategy2.DidNotReceive().VerifyAsync(Arg.Any<Milestone>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Test_Pipeline_ReturnsResults_FromAllStrategies()
    {
        // Arrange: three strategies, two applicable
        var strategy1 = Substitute.For<IVerificationStrategy>();
        strategy1.CanHandle(TaskType.Text).Returns(true);
        strategy1.StrategyType.Returns(VerificationStrategyType.TextSimilarity);
        strategy1.VerifyAsync(Arg.Any<Milestone>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new VerificationResult(0.8, true, "Good text", VerificationStrategyType.TextSimilarity));

        var strategy2 = Substitute.For<IVerificationStrategy>();
        strategy2.CanHandle(TaskType.Text).Returns(true);
        strategy2.StrategyType.Returns(VerificationStrategyType.AiJudge);
        strategy2.VerifyAsync(Arg.Any<Milestone>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new VerificationResult(0.6, false, "Needs improvement", VerificationStrategyType.AiJudge));

        var strategy3 = Substitute.For<IVerificationStrategy>();
        strategy3.CanHandle(TaskType.Text).Returns(false);
        strategy3.StrategyType.Returns(VerificationStrategyType.CodeCompile);

        var pipeline = new VerificationPipeline(new[] { strategy1, strategy2, strategy3 }, _logger);

        // VerificationCriteria is empty/null so defaults to Text
        var milestone = CreateMilestone("");

        // Act
        var results = await pipeline.RunVerificationAsync(milestone, new byte[] { 1, 2, 3 });

        // Assert: results from both applicable strategies
        results.Should().HaveCount(2);
        results.Should().Contain(r => r.StrategyType == VerificationStrategyType.TextSimilarity && r.Passed);
        results.Should().Contain(r => r.StrategyType == VerificationStrategyType.AiJudge && !r.Passed);
    }

    private static Milestone CreateMilestone(string verificationCriteria) => new()
    {
        Id = 1,
        TaskId = 1,
        SequenceNumber = 1,
        Title = "Test Milestone",
        VerificationCriteria = verificationCriteria,
        PayoutSats = 1000,
        Status = MilestoneStatus.Pending,
        CreatedAt = DateTime.UtcNow
    };
}
