using FluentAssertions;
using LightningAgent.Core.Configuration;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using LightningAgent.Engine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LightningAgent.Tests.Unit;

public class SpendLimitServiceTests
{
    private readonly ISpendLimitRepository _spendLimitRepo;
    private readonly ILogger<SpendLimitService> _logger;
    private readonly SpendLimitSettings _settings;
    private readonly SpendLimitService _sut;

    public SpendLimitServiceTests()
    {
        _spendLimitRepo = Substitute.For<ISpendLimitRepository>();
        _logger = Substitute.For<ILogger<SpendLimitService>>();
        _settings = new SpendLimitSettings
        {
            DefaultDailyCapSats = 1_000_000,
            DefaultWeeklyCapSats = 5_000_000,
            DefaultPerTaskMaxSats = 500_000
        };
        var options = Substitute.For<IOptions<SpendLimitSettings>>();
        options.Value.Returns(_settings);
        _sut = new SpendLimitService(_spendLimitRepo, options, _logger);
    }

    [Fact]
    public async Task Test_CheckLimit_ReturnsTrue_WhenUnderLimit()
    {
        // Arrange: agent has a limit with room to spare
        var limit = new SpendLimit
        {
            Id = 1,
            AgentId = 1,
            LimitType = "Daily",
            MaxSats = 100_000,
            CurrentSpentSats = 20_000,
            PeriodStart = DateTime.UtcNow.AddHours(-1),
            PeriodEnd = DateTime.UtcNow.AddHours(23)
        };
        _spendLimitRepo.GetByAgentIdAsync(1).Returns(limit);

        // Act
        var result = await _sut.CheckLimitAsync(1, 50_000);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task Test_CheckLimit_ReturnsFalse_WhenOverLimit()
    {
        // Arrange: agent is near limit
        var limit = new SpendLimit
        {
            Id = 1,
            AgentId = 1,
            LimitType = "Daily",
            MaxSats = 100_000,
            CurrentSpentSats = 90_000,
            PeriodStart = DateTime.UtcNow.AddHours(-1),
            PeriodEnd = DateTime.UtcNow.AddHours(23)
        };
        _spendLimitRepo.GetByAgentIdAsync(1).Returns(limit);

        // Act: requesting 20k would exceed 100k limit
        var result = await _sut.CheckLimitAsync(1, 20_000);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task Test_CheckLimit_UsesGlobalDefault_WhenNoAgentLimit()
    {
        // Arrange: no limit configured for agent
        _spendLimitRepo.GetByAgentIdAsync(99).Returns((SpendLimit?)null);

        // Act: amount under default daily cap (1_000_000)
        var resultUnder = await _sut.CheckLimitAsync(99, 500_000);

        // Assert
        resultUnder.Should().BeTrue();

        // Act: amount over default daily cap
        var resultOver = await _sut.CheckLimitAsync(99, 1_500_000);

        // Assert
        resultOver.Should().BeFalse();
    }

    [Fact]
    public async Task Test_RecordSpend_UpdatesCurrentSpent()
    {
        // Arrange: agent has existing limit
        var limit = new SpendLimit
        {
            Id = 1,
            AgentId = 1,
            LimitType = "Daily",
            MaxSats = 100_000,
            CurrentSpentSats = 30_000,
            PeriodStart = DateTime.UtcNow.AddHours(-1),
            PeriodEnd = DateTime.UtcNow.AddHours(23)
        };
        _spendLimitRepo.GetByAgentIdAsync(1).Returns(limit);

        // Act
        await _sut.RecordSpendAsync(1, 10_000);

        // Assert: update was called with incremented CurrentSpentSats
        await _spendLimitRepo.Received(1).UpdateAsync(Arg.Is<SpendLimit>(l => l.CurrentSpentSats == 40_000));
    }
}
