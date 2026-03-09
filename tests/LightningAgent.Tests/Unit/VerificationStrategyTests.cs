using System.Text;
using FluentAssertions;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Models;
using LightningAgent.Verification.Strategies;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LightningAgent.Tests.Unit;

public class VerificationStrategyTests
{
    private readonly Milestone _milestone = new()
    {
        Id = 1,
        TaskId = 1,
        SequenceNumber = 1,
        Title = "Test Milestone",
        VerificationCriteria = "{}",
        PayoutSats = 1000,
        Status = MilestoneStatus.Verifying,
        CreatedAt = DateTime.UtcNow
    };

    // --- CodeCompileVerification Tests ---

    [Fact]
    public async Task Test_CodeCompile_Passes_WellFormedCode()
    {
        // Arrange
        var logger = Substitute.For<ILogger<CodeCompileVerification>>();
        var strategy = new CodeCompileVerification(logger);

        var code = @"
using System;

namespace TestApp
{
    public class Calculator
    {
        public static int Add(int a, int b)
        {
            return a + b;
        }
    }
}";
        var output = Encoding.UTF8.GetBytes(code);

        // Act
        var result = await strategy.VerifyAsync(_milestone, output);

        // Assert
        result.Passed.Should().BeTrue();
        result.Score.Should().BeGreaterThanOrEqualTo(0.7);
        result.StrategyType.Should().Be(VerificationStrategyType.CodeCompile);
    }

    [Fact]
    public async Task Test_CodeCompile_Fails_EmptyOutput()
    {
        // Arrange
        var logger = Substitute.For<ILogger<CodeCompileVerification>>();
        var strategy = new CodeCompileVerification(logger);

        var output = Encoding.UTF8.GetBytes("");

        // Act
        var result = await strategy.VerifyAsync(_milestone, output);

        // Assert
        result.Passed.Should().BeFalse();
        result.Score.Should().BeLessThan(0.7);
    }

    // --- SchemaValidationVerification Tests ---

    [Fact]
    public async Task Test_SchemaValidation_Passes_ValidJson()
    {
        // Arrange
        var logger = Substitute.For<ILogger<SchemaValidationVerification>>();
        var strategy = new SchemaValidationVerification(logger);

        var json = """{"name": "test", "value": 42, "active": true}""";
        var output = Encoding.UTF8.GetBytes(json);

        // Act
        var result = await strategy.VerifyAsync(_milestone, output);

        // Assert
        result.Passed.Should().BeTrue();
        result.Score.Should().BeGreaterThanOrEqualTo(0.7);
        result.StrategyType.Should().Be(VerificationStrategyType.SchemaValidation);
    }

    [Fact]
    public async Task Test_SchemaValidation_Fails_InvalidJson()
    {
        // Arrange
        var logger = Substitute.For<ILogger<SchemaValidationVerification>>();
        var strategy = new SchemaValidationVerification(logger);

        var invalidJson = "this is not json at all {broken";
        var output = Encoding.UTF8.GetBytes(invalidJson);

        // Act
        var result = await strategy.VerifyAsync(_milestone, output);

        // Assert
        result.Passed.Should().BeFalse();
        result.Score.Should().Be(0.0);
        result.StrategyType.Should().Be(VerificationStrategyType.SchemaValidation);
    }

    // --- TextSimilarityVerification Tests ---

    [Fact]
    public async Task Test_TextSimilarity_Passes_LongText_WithKeywords()
    {
        // Arrange
        var logger = Substitute.For<ILogger<TextSimilarityVerification>>();
        var strategy = new TextSimilarityVerification(logger);

        var milestoneWithKeywords = new Milestone
        {
            Id = 1,
            TaskId = 1,
            SequenceNumber = 1,
            Title = "Test",
            VerificationCriteria = """{"keywords": ["lightning", "network", "payment"]}""",
            PayoutSats = 1000,
            Status = MilestoneStatus.Verifying,
            CreatedAt = DateTime.UtcNow
        };

        var text = "The Lightning Network is a revolutionary payment system built on top of Bitcoin. " +
                   "It enables instant payment transactions at extremely low fees. " +
                   "The network uses payment channels to facilitate lightning-fast transfers between parties. " +
                   "This technology represents a significant advancement in cryptocurrency infrastructure.";
        var output = Encoding.UTF8.GetBytes(text);

        // Act
        var result = await strategy.VerifyAsync(milestoneWithKeywords, output);

        // Assert
        result.Passed.Should().BeTrue();
        result.Score.Should().BeGreaterThanOrEqualTo(0.7);
        result.StrategyType.Should().Be(VerificationStrategyType.TextSimilarity);
    }

    [Fact]
    public async Task Test_TextSimilarity_Fails_ShortText()
    {
        // Arrange
        var logger = Substitute.For<ILogger<TextSimilarityVerification>>();
        var strategy = new TextSimilarityVerification(logger);

        var shortText = "Hello world.";
        var output = Encoding.UTF8.GetBytes(shortText);

        // Act
        var result = await strategy.VerifyAsync(_milestone, output);

        // Assert
        result.Passed.Should().BeFalse();
        result.Score.Should().BeLessThan(0.7);
    }

    // --- ClipScoreVerification Tests ---

    [Fact]
    public async Task Test_ClipScore_AlwaysFails_AsStub()
    {
        // Arrange
        var logger = Substitute.For<ILogger<ClipScoreVerification>>();
        var strategy = new ClipScoreVerification(logger);

        var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header stub

        // Act
        var result = await strategy.VerifyAsync(_milestone, imageData);

        // Assert: the stub always returns Passed=false
        result.Passed.Should().BeFalse();
        result.StrategyType.Should().Be(VerificationStrategyType.ClipScore);
        result.Details.Should().Contain("not configured");
    }
}
