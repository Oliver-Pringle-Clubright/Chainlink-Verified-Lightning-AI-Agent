using FluentAssertions;
using LightningAgent.Core.Configuration;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using LightningAgent.Core.Models.Lightning;
using LightningAgent.Data;
using LightningAgent.Data.Repositories;
using LightningAgent.Engine;
using LightningAgent.Chainlink.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LightningAgent.Tests.Integration;

public class EscrowLifecycleTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly ILightningClient _lightning;
    private readonly IEventPublisher _eventPublisher;
    private readonly EscrowRepository _escrowRepo;
    private readonly MilestoneRepository _milestoneRepo;
    private readonly EscrowManager _sut;

    public EscrowLifecycleTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory($"Data Source={_dbPath}");

        // Initialize DB
        var initializer = new DatabaseInitializer(_factory);
        initializer.InitializeAsync().GetAwaiter().GetResult();

        _lightning = Substitute.For<ILightningClient>();
        _eventPublisher = Substitute.For<IEventPublisher>();
        _escrowRepo = new EscrowRepository(_factory);
        _milestoneRepo = new MilestoneRepository(_factory);

        var settings = Substitute.For<IOptions<EscrowSettings>>();
        settings.Value.Returns(new EscrowSettings { DefaultExpirySec = 3600, MaxRetries = 2 });

        var logger = Substitute.For<ILogger<EscrowManager>>();

        var automationClient = Substitute.For<IChainlinkAutomationClient>();
        var chainlinkSettings = Substitute.For<IOptions<ChainlinkSettings>>();
        chainlinkSettings.Value.Returns(new ChainlinkSettings());
        var automation = new AutomationService(
            automationClient,
            chainlinkSettings,
            Substitute.For<ILogger<AutomationService>>());

        _sut = new EscrowManager(
            _lightning,
            _escrowRepo,
            _milestoneRepo,
            _eventPublisher,
            automation,
            settings,
            logger);
    }

    public void Dispose()
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    private async Task<(int taskId, int milestoneId)> SeedTaskAndMilestoneAsync()
    {
        var taskRepo = new TaskRepository(_factory);
        var taskId = await taskRepo.CreateAsync(new TaskItem
        {
            ExternalId = $"ext-t-{Guid.NewGuid():N}",
            ClientId = "client-1",
            Title = "Task",
            Description = "Desc",
            TaskType = TaskType.Code,
            Status = Core.Enums.TaskStatus.Pending,
            MaxPayoutSats = 10000,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var milestoneId = await _milestoneRepo.CreateAsync(new Milestone
        {
            TaskId = taskId,
            SequenceNumber = 1,
            Title = "Milestone",
            VerificationCriteria = "{}",
            PayoutSats = 5000,
            Status = MilestoneStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });

        return (taskId, milestoneId);
    }

    [Fact]
    public async Task Test_CreateEscrow_CreatesHodlInvoice()
    {
        // Arrange
        var (taskId, milestoneId) = await SeedTaskAndMilestoneAsync();
        var milestone = await _milestoneRepo.GetByIdAsync(milestoneId);

        _lightning.CreateHodlInvoiceAsync(
            Arg.Any<long>(),
            Arg.Any<string>(),
            Arg.Any<byte[]>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo => new HodlInvoice
            {
                PaymentHash = "hash123",
                PaymentRequest = "lnbc5000...",
                AmountSats = callInfo.ArgAt<long>(0),
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                State = "OPEN"
            });

        // Act
        var escrow = await _sut.CreateEscrowAsync(milestone!);

        // Assert
        escrow.Should().NotBeNull();
        escrow.Status.Should().Be(EscrowStatus.Held);
        escrow.AmountSats.Should().Be(5000);
        escrow.HodlInvoice.Should().Be("lnbc5000...");
        escrow.PaymentHash.Should().NotBeNullOrEmpty();
        escrow.PaymentPreimage.Should().NotBeNullOrEmpty();

        // Verify persisted
        var persisted = await _escrowRepo.GetByIdAsync(escrow.Id);
        persisted.Should().NotBeNull();
        persisted!.Status.Should().Be(EscrowStatus.Held);

        // Verify Lightning was called
        await _lightning.Received(1).CreateHodlInvoiceAsync(
            5000,
            Arg.Any<string>(),
            Arg.Any<byte[]>(),
            3600,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Test_SettleEscrow_ReleasesPayment()
    {
        // Arrange: create an escrow first
        var (taskId, milestoneId) = await SeedTaskAndMilestoneAsync();
        var milestone = await _milestoneRepo.GetByIdAsync(milestoneId);

        _lightning.CreateHodlInvoiceAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new HodlInvoice
            {
                PaymentHash = "hash123",
                PaymentRequest = "lnbc5000...",
                AmountSats = 5000,
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                State = "OPEN"
            });

        var escrow = await _sut.CreateEscrowAsync(milestone!);

        _lightning.SettleInvoiceAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var preimage = Convert.FromHexString(escrow.PaymentPreimage!);

        // Act
        var result = await _sut.SettleEscrowAsync(escrow.Id, preimage);

        // Assert
        result.Should().BeTrue();

        var settled = await _escrowRepo.GetByIdAsync(escrow.Id);
        settled.Should().NotBeNull();
        settled!.Status.Should().Be(EscrowStatus.Settled);
        settled.SettledAt.Should().NotBeNull();

        // Verify Lightning settle was called
        await _lightning.Received(1).SettleInvoiceAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>());

        // Verify event was published
        await _eventPublisher.Received(1).PublishEscrowSettledAsync(
            escrow.Id, milestoneId, 5000, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Test_CancelEscrow_RefundsPayment()
    {
        // Arrange
        var (taskId, milestoneId) = await SeedTaskAndMilestoneAsync();
        var milestone = await _milestoneRepo.GetByIdAsync(milestoneId);

        _lightning.CreateHodlInvoiceAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new HodlInvoice
            {
                PaymentHash = "hash123",
                PaymentRequest = "lnbc5000...",
                AmountSats = 5000,
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                State = "OPEN"
            });

        var escrow = await _sut.CreateEscrowAsync(milestone!);

        _lightning.CancelInvoiceAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _sut.CancelEscrowAsync(escrow.Id);

        // Assert
        result.Should().BeTrue();

        var cancelled = await _escrowRepo.GetByIdAsync(escrow.Id);
        cancelled.Should().NotBeNull();
        cancelled!.Status.Should().Be(EscrowStatus.Cancelled);

        // Verify Lightning cancel was called
        await _lightning.Received(1).CancelInvoiceAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Test_ExpiredEscrow_GetsCancelled()
    {
        // Arrange: manually insert an expired escrow directly into the database
        var (taskId, milestoneId) = await SeedTaskAndMilestoneAsync();

        var expiredEscrow = new Escrow
        {
            MilestoneId = milestoneId,
            TaskId = taskId,
            AmountSats = 5000,
            PaymentHash = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789",
            PaymentPreimage = "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef",
            Status = EscrowStatus.Held,
            HodlInvoice = "lnbc5000_expired...",
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            ExpiresAt = DateTime.UtcNow.AddHours(-1) // Already expired
        };

        var escrowId = await _escrowRepo.CreateAsync(expiredEscrow);

        _lightning.CancelInvoiceAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var cancelledCount = await _sut.CheckExpiredEscrowsAsync();

        // Assert
        cancelledCount.Should().Be(1);

        var escrow = await _escrowRepo.GetByIdAsync(escrowId);
        escrow.Should().NotBeNull();
        escrow!.Status.Should().Be(EscrowStatus.Cancelled);
    }
}
