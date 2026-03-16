using FluentAssertions;
using LightningAgentMarketPlace.Core.Enums;
using LightningAgentMarketPlace.Core.Models;
using LightningAgentMarketPlace.Data;
using LightningAgentMarketPlace.Data.Repositories;
using Microsoft.Data.Sqlite;

namespace LightningAgentMarketPlace.Tests.Integration;

public class DatabaseTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;

    public DatabaseTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory($"Data Source={_dbPath}");
    }

    public void Dispose()
    {
        try
        {
            // Close all connections before deleting
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    private async Task InitializeDbAsync()
    {
        var initializer = new DatabaseInitializer(_factory);
        await initializer.InitializeAsync();
    }

    [Fact]
    public async Task Test_DatabaseInitializer_CreatesAllTables()
    {
        // Act
        await InitializeDbAsync();

        // Assert: check that expected tables exist
        using var connection = _factory.CreateConnection();
        var tables = new List<string>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        tables.Should().Contain("Agents");
        tables.Should().Contain("AgentCapabilities");
        tables.Should().Contain("AgentReputation");
        tables.Should().Contain("Tasks");
        tables.Should().Contain("Milestones");
        tables.Should().Contain("Escrows");
        tables.Should().Contain("Payments");
        tables.Should().Contain("Verifications");
        tables.Should().Contain("Disputes");
        tables.Should().Contain("PriceCache");
        tables.Should().Contain("AuditLog");
        tables.Should().Contain("SpendLimits");
        tables.Should().Contain("VerificationStrategyConfig");
    }

    [Fact]
    public async Task Test_AgentRepository_CreateAndRetrieve()
    {
        // Arrange
        await InitializeDbAsync();
        var repo = new AgentRepository(_factory);

        var agent = new Agent
        {
            ExternalId = "ext-agent-1",
            Name = "TestAgent",
            WalletPubkey = "02abc123",
            Status = AgentStatus.Active,
            DailySpendCapSats = 100_000,
            WeeklySpendCapSats = 500_000,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Act
        var id = await repo.CreateAsync(agent);
        var retrieved = await repo.GetByIdAsync(id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(id);
        retrieved.ExternalId.Should().Be("ext-agent-1");
        retrieved.Name.Should().Be("TestAgent");
        retrieved.WalletPubkey.Should().Be("02abc123");
        retrieved.Status.Should().Be(AgentStatus.Active);
        retrieved.DailySpendCapSats.Should().Be(100_000);
    }

    [Fact]
    public async Task Test_TaskRepository_CreateAndRetrieve()
    {
        // Arrange
        await InitializeDbAsync();
        var taskRepo = new TaskRepository(_factory);

        var task = new TaskItem
        {
            ExternalId = "ext-task-1",
            ClientId = "client-1",
            Title = "Code Generation Task",
            Description = "Generate a sorting algorithm",
            TaskType = TaskType.Code,
            Status = Core.Enums.TaskStatus.Pending,
            MaxPayoutSats = 50_000,
            ActualPayoutSats = 0,
            Priority = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Act
        var id = await taskRepo.CreateAsync(task);
        var retrieved = await taskRepo.GetByIdAsync(id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(id);
        retrieved.ExternalId.Should().Be("ext-task-1");
        retrieved.Title.Should().Be("Code Generation Task");
        retrieved.TaskType.Should().Be(TaskType.Code);
        retrieved.Status.Should().Be(Core.Enums.TaskStatus.Pending);
        retrieved.MaxPayoutSats.Should().Be(50_000);
    }

    [Fact]
    public async Task Test_EscrowRepository_CreateAndUpdateStatus()
    {
        // Arrange
        await InitializeDbAsync();

        // Need agent and task first for foreign keys
        var agentRepo = new AgentRepository(_factory);
        var agentId = await agentRepo.CreateAsync(new Agent
        {
            ExternalId = "ext-a1",
            Name = "Agent1",
            Status = AgentStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var taskRepo = new TaskRepository(_factory);
        var taskId = await taskRepo.CreateAsync(new TaskItem
        {
            ExternalId = "ext-t1",
            ClientId = "client-1",
            Title = "Task1",
            Description = "Desc",
            TaskType = TaskType.Code,
            Status = Core.Enums.TaskStatus.Pending,
            MaxPayoutSats = 10000,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var milestoneRepo = new MilestoneRepository(_factory);
        var milestoneId = await milestoneRepo.CreateAsync(new Milestone
        {
            TaskId = taskId,
            SequenceNumber = 1,
            Title = "Milestone1",
            VerificationCriteria = "{}",
            PayoutSats = 5000,
            Status = MilestoneStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });

        var escrowRepo = new EscrowRepository(_factory);
        var escrow = new Escrow
        {
            MilestoneId = milestoneId,
            TaskId = taskId,
            AmountSats = 5000,
            PaymentHash = "abc123def456",
            PaymentPreimage = "preimage123",
            Status = EscrowStatus.Held,
            HodlInvoice = "lnbc50000...",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        // Act
        var escrowId = await escrowRepo.CreateAsync(escrow);
        var retrieved = await escrowRepo.GetByIdAsync(escrowId);

        // Assert: created successfully
        retrieved.Should().NotBeNull();
        retrieved!.Status.Should().Be(EscrowStatus.Held);
        retrieved.AmountSats.Should().Be(5000);
        retrieved.PaymentHash.Should().Be("abc123def456");

        // Act: update status
        await escrowRepo.UpdateStatusAsync(escrowId, EscrowStatus.Settled);
        var updated = await escrowRepo.GetByIdAsync(escrowId);

        // Assert: status changed
        updated.Should().NotBeNull();
        updated!.Status.Should().Be(EscrowStatus.Settled);
    }

    [Fact]
    public async Task Test_MilestoneRepository_CreateAndGetByTask()
    {
        // Arrange
        await InitializeDbAsync();

        var taskRepo = new TaskRepository(_factory);
        var taskId = await taskRepo.CreateAsync(new TaskItem
        {
            ExternalId = "ext-t1",
            ClientId = "client-1",
            Title = "Task1",
            Description = "Desc",
            TaskType = TaskType.Code,
            Status = Core.Enums.TaskStatus.Pending,
            MaxPayoutSats = 10000,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var milestoneRepo = new MilestoneRepository(_factory);

        var m1 = new Milestone
        {
            TaskId = taskId,
            SequenceNumber = 1,
            Title = "Step 1",
            VerificationCriteria = """{"taskType": "Code"}""",
            PayoutSats = 3000,
            Status = MilestoneStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        var m2 = new Milestone
        {
            TaskId = taskId,
            SequenceNumber = 2,
            Title = "Step 2",
            VerificationCriteria = """{"taskType": "Code"}""",
            PayoutSats = 7000,
            Status = MilestoneStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await milestoneRepo.CreateAsync(m1);
        await milestoneRepo.CreateAsync(m2);
        var milestones = await milestoneRepo.GetByTaskIdAsync(taskId);

        // Assert
        milestones.Should().HaveCount(2);
        milestones[0].SequenceNumber.Should().Be(1);
        milestones[1].SequenceNumber.Should().Be(2);
        milestones[0].Title.Should().Be("Step 1");
        milestones[1].Title.Should().Be("Step 2");
    }
}
