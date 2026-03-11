using FluentAssertions;
using LightningAgent.AI.Orchestrator;
using LightningAgent.Core.Configuration;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using LightningAgent.Core.Models.AI;
using LightningAgent.Core.Models.Lightning;
using LightningAgent.Data;
using LightningAgent.Data.Repositories;
using LightningAgent.Engine;
using LightningAgent.Chainlink.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TaskStatus = LightningAgent.Core.Enums.TaskStatus;

namespace LightningAgent.Tests.Integration;

public class EndToEndWorkflowTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;

    // Real repositories backed by SQLite
    private readonly TaskRepository _taskRepo;
    private readonly AgentRepository _agentRepo;
    private readonly MilestoneRepository _milestoneRepo;
    private readonly AgentCapabilityRepository _capabilityRepo;
    private readonly EscrowRepository _escrowRepo;
    private readonly VerificationRepository _verificationRepo;

    // Mocked external services
    private readonly IClaudeAiClient _claudeAi;
    private readonly ILightningClient _lightning;
    private readonly IChainlinkFunctionsClient _chainlinkFunctions;
    private readonly IChainlinkVrfClient _chainlinkVrf;
    private readonly IChainlinkPriceFeedClient _chainlinkPriceFeed;
    private readonly IChainlinkAutomationClient _chainlinkAutomation;
    private readonly IEventPublisher _eventPublisher;
    private readonly IAgentMatcher _agentMatcher;
    private readonly IEscrowManager _escrowManager;
    private readonly IVerificationPipeline _verificationPipeline;
    private readonly IPaymentService _paymentService;
    private readonly IReputationService _reputationService;
    private readonly ISpendLimitService _spendLimitService;
    private readonly IMetricsCollector _metricsCollector;

    public EndToEndWorkflowTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory($"Data Source={_dbPath}");

        // Initialize database schema
        var initializer = new DatabaseInitializer(_factory);
        initializer.InitializeAsync().GetAwaiter().GetResult();

        // Real repositories
        _taskRepo = new TaskRepository(_factory);
        _agentRepo = new AgentRepository(_factory);
        _milestoneRepo = new MilestoneRepository(_factory);
        _capabilityRepo = new AgentCapabilityRepository(_factory);
        _escrowRepo = new EscrowRepository(_factory);
        _verificationRepo = new VerificationRepository(_factory);

        // Mocked external services
        _claudeAi = Substitute.For<IClaudeAiClient>();
        _lightning = Substitute.For<ILightningClient>();
        _chainlinkFunctions = Substitute.For<IChainlinkFunctionsClient>();
        _chainlinkVrf = Substitute.For<IChainlinkVrfClient>();
        _chainlinkPriceFeed = Substitute.For<IChainlinkPriceFeedClient>();
        _chainlinkAutomation = Substitute.For<IChainlinkAutomationClient>();
        _eventPublisher = Substitute.For<IEventPublisher>();
        _agentMatcher = Substitute.For<IAgentMatcher>();
        _escrowManager = Substitute.For<IEscrowManager>();
        _verificationPipeline = Substitute.For<IVerificationPipeline>();
        _paymentService = Substitute.For<IPaymentService>();
        _reputationService = Substitute.For<IReputationService>();
        _spendLimitService = Substitute.For<ISpendLimitService>();
        _metricsCollector = Substitute.For<IMetricsCollector>();
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

    /// <summary>
    /// Creates a TaskDecompositionEngine wired to real repos and a mocked Claude AI client.
    /// </summary>
    private TaskDecompositionEngine CreateDecompositionEngine()
    {
        var decomposer = new TaskDecomposer(
            _claudeAi,
            Substitute.For<ILogger<TaskDecomposer>>());

        return new TaskDecompositionEngine(
            decomposer,
            _taskRepo,
            _milestoneRepo,
            Substitute.For<ILogger<TaskDecompositionEngine>>());
    }

    /// <summary>
    /// Creates a TaskOrchestrator wired to real repos and mocked services.
    /// </summary>
    private TaskOrchestrator CreateOrchestrator()
    {
        var decompositionEngine = CreateDecompositionEngine();

        var assembler = new DeliverableAssembler(
            _claudeAi,
            Substitute.For<ILogger<DeliverableAssembler>>());

        var automationClient = Substitute.For<IChainlinkAutomationClient>();
        var chainlinkSettings = Substitute.For<IOptions<ChainlinkSettings>>();
        chainlinkSettings.Value.Returns(new ChainlinkSettings());
        var automation = new AutomationService(
            automationClient,
            chainlinkSettings,
            Substitute.For<ILogger<AutomationService>>());

        return new TaskOrchestrator(
            decompositionEngine,
            _agentMatcher,
            _escrowManager,
            _verificationPipeline,
            _paymentService,
            _reputationService,
            _spendLimitService,
            _eventPublisher,
            automation,
            assembler,
            _taskRepo,
            _milestoneRepo,
            _verificationRepo,
            _metricsCollector,
            Substitute.For<ILogger<TaskOrchestrator>>());
    }

    /// <summary>
    /// Seeds an agent with a CodeGeneration capability into the database.
    /// Returns the persisted agent ID.
    /// </summary>
    private async Task<int> SeedAgentWithCapabilitiesAsync()
    {
        var agent = new Agent
        {
            ExternalId = $"ext-agent-{Guid.NewGuid():N}",
            Name = "CodeBot",
            WalletPubkey = "02abcdef1234567890",
            Status = AgentStatus.Active,
            DailySpendCapSats = 500_000,
            WeeklySpendCapSats = 2_000_000,
            RateLimitPerMinute = 60,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var agentId = await _agentRepo.CreateAsync(agent);

        await _capabilityRepo.CreateAsync(new AgentCapability
        {
            AgentId = agentId,
            SkillType = SkillType.CodeGeneration,
            TaskTypes = "Code",
            MaxConcurrency = 3,
            PriceSatsPerUnit = 1000,
            AvgResponseSec = 30,
            CreatedAt = DateTime.UtcNow
        });

        return agentId;
    }

    /// <summary>
    /// Seeds a parent task in Pending status.
    /// </summary>
    private async Task<int> SeedParentTaskAsync()
    {
        var task = new TaskItem
        {
            ExternalId = $"ext-task-{Guid.NewGuid():N}",
            ClientId = "client-integration",
            Title = "Build a sorting library",
            Description = "Implement quicksort and mergesort with unit tests",
            TaskType = TaskType.Code,
            Status = TaskStatus.Pending,
            VerificationCriteria = "All unit tests pass, code compiles",
            MaxPayoutSats = 50_000,
            ActualPayoutSats = 0,
            Priority = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        return await _taskRepo.CreateAsync(task);
    }

    [Fact]
    public async Task Full_Task_Lifecycle_From_Creation_To_Completion()
    {
        // ---------------------------------------------------------------
        // STEP 1: Seed an agent with capabilities
        // ---------------------------------------------------------------
        var agentId = await SeedAgentWithCapabilitiesAsync();

        var agent = await _agentRepo.GetByIdAsync(agentId);
        agent.Should().NotBeNull();
        agent!.Name.Should().Be("CodeBot");
        agent.Status.Should().Be(AgentStatus.Active);

        var capabilities = await _capabilityRepo.GetByAgentIdAsync(agentId);
        capabilities.Should().HaveCount(1);
        capabilities[0].SkillType.Should().Be(SkillType.CodeGeneration);

        // ---------------------------------------------------------------
        // STEP 2: Create a parent task
        // ---------------------------------------------------------------
        var parentTaskId = await SeedParentTaskAsync();

        var parentTask = await _taskRepo.GetByIdAsync(parentTaskId);
        parentTask.Should().NotBeNull();
        parentTask!.Status.Should().Be(TaskStatus.Pending);
        parentTask.Title.Should().Be("Build a sorting library");

        // ---------------------------------------------------------------
        // STEP 3: Configure mocks for orchestration
        // ---------------------------------------------------------------

        // 3a. Claude AI returns a plan with 1 subtask
        var plan = new OrchestrationPlan
        {
            OriginalTaskId = parentTaskId.ToString(),
            Subtasks = new List<PlannedSubtask>
            {
                new PlannedSubtask
                {
                    Title = "Implement sorting algorithms",
                    Description = "Write quicksort and mergesort in C#",
                    TaskType = TaskType.Code,
                    RequiredSkills = new List<string> { "CodeGeneration" },
                    EstimatedSats = 25_000,
                    VerificationCriteria = "Code compiles and tests pass"
                }
            },
            EstimatedTotalSats = 25_000,
            EstimatedTotalTimeSec = 120
        };

        _claudeAi.SendStructuredRequestAsync<OrchestrationPlan>(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(plan);

        // 3b. Agent matcher returns our seeded agent as the best match
        _agentMatcher.FindBestAgentAsync(Arg.Any<TaskItem>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var matchedAgent = new Agent
                {
                    Id = agentId,
                    ExternalId = agent.ExternalId,
                    Name = agent.Name,
                    Status = AgentStatus.Active,
                    DailySpendCapSats = agent.DailySpendCapSats,
                    WeeklySpendCapSats = agent.WeeklySpendCapSats,
                    CreatedAt = agent.CreatedAt,
                    UpdatedAt = agent.UpdatedAt
                };
                return new List<(Agent Agent, double MatchScore)>
                {
                    (matchedAgent, 0.95)
                };
            });

        // 3c. Spend limit check passes
        _spendLimitService.CheckLimitAsync(
            Arg.Any<int>(),
            Arg.Any<long>(),
            Arg.Any<CancellationToken>())
            .Returns(true);

        // 3d. Escrow manager creates an escrow for the milestone
        _escrowManager.CreateEscrowAsync(Arg.Any<Milestone>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var milestone = callInfo.ArgAt<Milestone>(0);
                return new Escrow
                {
                    Id = 1,
                    MilestoneId = milestone.Id,
                    TaskId = milestone.TaskId,
                    AmountSats = milestone.PayoutSats,
                    PaymentHash = "hash_" + Guid.NewGuid().ToString("N"),
                    PaymentPreimage = "preimage_" + Guid.NewGuid().ToString("N"),
                    Status = EscrowStatus.Held,
                    HodlInvoice = "lnbc25000...",
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddHours(1)
                };
            });

        // 3e. Event publisher does nothing (fire-and-forget)
        _eventPublisher.PublishTaskAssignedAsync(
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // ---------------------------------------------------------------
        // STEP 4: Orchestrate the task
        // ---------------------------------------------------------------
        var orchestrator = CreateOrchestrator();
        var orchestratedTask = await orchestrator.OrchestrateTaskAsync(parentTask);

        // ---------------------------------------------------------------
        // STEP 5: Verify the parent task is now InProgress
        // ---------------------------------------------------------------
        orchestratedTask.Status.Should().Be(TaskStatus.InProgress);

        var parentAfterOrchestration = await _taskRepo.GetByIdAsync(parentTaskId);
        parentAfterOrchestration.Should().NotBeNull();
        parentAfterOrchestration!.Status.Should().Be(TaskStatus.InProgress);

        // ---------------------------------------------------------------
        // STEP 6: Verify subtask was created and assigned
        // ---------------------------------------------------------------
        var subtasks = await _taskRepo.GetSubtasksAsync(parentTaskId);
        subtasks.Should().HaveCount(1);

        var subtask = subtasks[0];
        subtask.Title.Should().Be("Implement sorting algorithms");
        subtask.ParentTaskId.Should().Be(parentTaskId);
        subtask.MaxPayoutSats.Should().Be(25_000);
        subtask.AssignedAgentId.Should().Be(agentId);
        // After orchestration, the subtask should be InProgress
        // (it was set to Assigned, then to InProgress)
        subtask.Status.Should().Be(TaskStatus.InProgress);

        // ---------------------------------------------------------------
        // STEP 7: Verify milestone was created for the subtask
        // ---------------------------------------------------------------
        var milestones = await _milestoneRepo.GetByTaskIdAsync(subtask.Id);
        milestones.Should().HaveCount(1);

        var milestone = milestones[0];
        milestone.TaskId.Should().Be(subtask.Id);
        milestone.Title.Should().Be("Implement sorting algorithms");
        milestone.PayoutSats.Should().Be(25_000);
        milestone.SequenceNumber.Should().Be(1);
        milestone.Status.Should().Be(MilestoneStatus.Pending);
        milestone.VerificationCriteria.Should().Be("Code compiles and tests pass");

        // ---------------------------------------------------------------
        // STEP 8: Verify escrow was created for the milestone
        // ---------------------------------------------------------------
        await _escrowManager.Received(1).CreateEscrowAsync(
            Arg.Is<Milestone>(m => m.Id == milestone.Id),
            Arg.Any<CancellationToken>());

        // ---------------------------------------------------------------
        // STEP 9: Verify event was published for task assignment
        // ---------------------------------------------------------------
        await _eventPublisher.Received(1).PublishTaskAssignedAsync(
            subtask.Id,
            agentId,
            Arg.Any<CancellationToken>());

        // ---------------------------------------------------------------
        // STEP 10: Simulate milestone output submission
        //   (agent completes work and submits output data)
        // ---------------------------------------------------------------
        var outputText = "public static class Sorting { /* quicksort + mergesort */ }";
        var outputBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(outputText));

        milestone.OutputData = outputBase64;
        milestone.Status = MilestoneStatus.Passed;
        milestone.VerifiedAt = DateTime.UtcNow;
        await _milestoneRepo.UpdateAsync(milestone);

        // Verify the milestone was updated in the database
        var updatedMilestone = await _milestoneRepo.GetByIdAsync(milestone.Id);
        updatedMilestone.Should().NotBeNull();
        updatedMilestone!.Status.Should().Be(MilestoneStatus.Passed);
        updatedMilestone.OutputData.Should().Be(outputBase64);
        updatedMilestone.VerifiedAt.Should().NotBeNull();

        // ---------------------------------------------------------------
        // STEP 11: Check task completion via CheckAndCompleteTaskAsync
        //   Configure assembler mock to return assembled deliverable
        // ---------------------------------------------------------------
        _claudeAi.SendMessageAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns("Final assembled deliverable: sorting library complete.");

        var isComplete = await orchestrator.CheckAndCompleteTaskAsync(parentTaskId);

        // ---------------------------------------------------------------
        // STEP 12: Assert task completion and all state transitions
        // ---------------------------------------------------------------
        isComplete.Should().BeTrue();

        var completedParent = await _taskRepo.GetByIdAsync(parentTaskId);
        completedParent.Should().NotBeNull();
        completedParent!.Status.Should().Be(TaskStatus.Completed);

        // Subtask should also be marked as Completed
        var completedSubtask = await _taskRepo.GetByIdAsync(subtask.Id);
        completedSubtask.Should().NotBeNull();
        completedSubtask!.Status.Should().Be(TaskStatus.Completed);

        // ---------------------------------------------------------------
        // STEP 13: Verify the deliverable assembly was invoked
        // ---------------------------------------------------------------
        await _claudeAi.Received(1).SendMessageAsync(
            Arg.Any<string>(),
            Arg.Is<string>(msg => msg.Contains(outputText)),
            Arg.Any<CancellationToken>());

        // ---------------------------------------------------------------
        // STEP 14: Verify spend limit was checked during orchestration
        // ---------------------------------------------------------------
        await _spendLimitService.Received(1).CheckLimitAsync(
            agentId,
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Orchestration_With_No_Matching_Agents_Leaves_Subtask_Unassigned()
    {
        // Arrange
        var agentId = await SeedAgentWithCapabilitiesAsync();
        var parentTaskId = await SeedParentTaskAsync();
        var parentTask = await _taskRepo.GetByIdAsync(parentTaskId);

        var plan = new OrchestrationPlan
        {
            Subtasks = new List<PlannedSubtask>
            {
                new PlannedSubtask
                {
                    Title = "Subtask with no agents",
                    Description = "Nobody can do this",
                    TaskType = TaskType.Code,
                    EstimatedSats = 10_000,
                    VerificationCriteria = "N/A"
                }
            },
            EstimatedTotalSats = 10_000
        };

        _claudeAi.SendStructuredRequestAsync<OrchestrationPlan>(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(plan);

        // No matching agents
        _agentMatcher.FindBestAgentAsync(Arg.Any<TaskItem>(), Arg.Any<CancellationToken>())
            .Returns(new List<(Agent Agent, double MatchScore)>());

        var orchestrator = CreateOrchestrator();

        // Act
        var result = await orchestrator.OrchestrateTaskAsync(parentTask!);

        // Assert: parent is InProgress (orchestration setup completed)
        result.Status.Should().Be(TaskStatus.InProgress);

        // Subtask should exist but remain Pending (not assigned)
        var subtasks = await _taskRepo.GetSubtasksAsync(parentTaskId);
        subtasks.Should().HaveCount(1);
        subtasks[0].Status.Should().Be(TaskStatus.Pending);
        subtasks[0].AssignedAgentId.Should().BeNull();

        // No escrow should have been created
        await _escrowManager.DidNotReceive().CreateEscrowAsync(
            Arg.Any<Milestone>(), Arg.Any<CancellationToken>());

        // No task-assigned event should have been published
        await _eventPublisher.DidNotReceive().PublishTaskAssignedAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Orchestration_SpendLimit_Exceeded_Skips_Assignment()
    {
        // Arrange
        var agentId = await SeedAgentWithCapabilitiesAsync();
        var agent = await _agentRepo.GetByIdAsync(agentId);
        var parentTaskId = await SeedParentTaskAsync();
        var parentTask = await _taskRepo.GetByIdAsync(parentTaskId);

        var plan = new OrchestrationPlan
        {
            Subtasks = new List<PlannedSubtask>
            {
                new PlannedSubtask
                {
                    Title = "Expensive subtask",
                    Description = "Over budget",
                    TaskType = TaskType.Code,
                    EstimatedSats = 999_999,
                    VerificationCriteria = "criteria"
                }
            },
            EstimatedTotalSats = 999_999
        };

        _claudeAi.SendStructuredRequestAsync<OrchestrationPlan>(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(plan);

        _agentMatcher.FindBestAgentAsync(Arg.Any<TaskItem>(), Arg.Any<CancellationToken>())
            .Returns(new List<(Agent Agent, double MatchScore)> { (agent!, 0.9) });

        // Spend limit check fails
        _spendLimitService.CheckLimitAsync(
            Arg.Any<int>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var orchestrator = CreateOrchestrator();

        // Act
        var result = await orchestrator.OrchestrateTaskAsync(parentTask!);

        // Assert: orchestration proceeds but subtask is not assigned
        result.Status.Should().Be(TaskStatus.InProgress);

        var subtasks = await _taskRepo.GetSubtasksAsync(parentTaskId);
        subtasks.Should().HaveCount(1);
        subtasks[0].AssignedAgentId.Should().BeNull();
        subtasks[0].Status.Should().Be(TaskStatus.Pending);

        // No escrow created because agent was not assigned
        await _escrowManager.DidNotReceive().CreateEscrowAsync(
            Arg.Any<Milestone>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Decomposition_Failure_Marks_Task_As_Failed()
    {
        // Arrange
        var parentTaskId = await SeedParentTaskAsync();
        var parentTask = await _taskRepo.GetByIdAsync(parentTaskId);

        // Claude AI throws during decomposition
        _claudeAi.SendStructuredRequestAsync<OrchestrationPlan>(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("AI service unavailable"));

        var orchestrator = CreateOrchestrator();

        // Act
        var result = await orchestrator.OrchestrateTaskAsync(parentTask!);

        // Assert
        result.Status.Should().Be(TaskStatus.Failed);

        var failedTask = await _taskRepo.GetByIdAsync(parentTaskId);
        failedTask.Should().NotBeNull();
        failedTask!.Status.Should().Be(TaskStatus.Failed);

        // No subtasks should have been created
        var subtasks = await _taskRepo.GetSubtasksAsync(parentTaskId);
        subtasks.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckAndCompleteTask_With_Failed_Milestones_Marks_Task_Failed()
    {
        // Arrange: create parent task, a subtask, and a failed milestone directly
        var parentTaskId = await SeedParentTaskAsync();
        await _taskRepo.UpdateStatusAsync(parentTaskId, TaskStatus.InProgress);

        var subtask = new TaskItem
        {
            ExternalId = $"ext-sub-{Guid.NewGuid():N}",
            ParentTaskId = parentTaskId,
            ClientId = "client-integration",
            Title = "Subtask that fails",
            Description = "This subtask will fail",
            TaskType = TaskType.Code,
            Status = TaskStatus.InProgress,
            MaxPayoutSats = 10_000,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var subtaskId = await _taskRepo.CreateAsync(subtask);

        var milestone = new Milestone
        {
            TaskId = subtaskId,
            SequenceNumber = 1,
            Title = "Failed milestone",
            VerificationCriteria = "Tests must pass",
            PayoutSats = 10_000,
            Status = MilestoneStatus.Failed,
            CreatedAt = DateTime.UtcNow
        };
        await _milestoneRepo.CreateAsync(milestone);

        var orchestrator = CreateOrchestrator();

        // Act
        var isComplete = await orchestrator.CheckAndCompleteTaskAsync(parentTaskId);

        // Assert
        isComplete.Should().BeTrue();

        var failedParent = await _taskRepo.GetByIdAsync(parentTaskId);
        failedParent.Should().NotBeNull();
        failedParent!.Status.Should().Be(TaskStatus.Failed);
    }

    [Fact]
    public async Task CheckAndCompleteTask_With_Pending_Milestones_Returns_False()
    {
        // Arrange: parent task with a still-pending milestone
        var parentTaskId = await SeedParentTaskAsync();
        await _taskRepo.UpdateStatusAsync(parentTaskId, TaskStatus.InProgress);

        var subtask = new TaskItem
        {
            ExternalId = $"ext-sub-{Guid.NewGuid():N}",
            ParentTaskId = parentTaskId,
            ClientId = "client-integration",
            Title = "Subtask still working",
            Description = "Not done yet",
            TaskType = TaskType.Code,
            Status = TaskStatus.InProgress,
            MaxPayoutSats = 10_000,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var subtaskId = await _taskRepo.CreateAsync(subtask);

        await _milestoneRepo.CreateAsync(new Milestone
        {
            TaskId = subtaskId,
            SequenceNumber = 1,
            Title = "Pending milestone",
            VerificationCriteria = "criteria",
            PayoutSats = 10_000,
            Status = MilestoneStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });

        var orchestrator = CreateOrchestrator();

        // Act
        var isComplete = await orchestrator.CheckAndCompleteTaskAsync(parentTaskId);

        // Assert: task is not yet complete because milestone is still Pending
        isComplete.Should().BeFalse();

        var parentStillInProgress = await _taskRepo.GetByIdAsync(parentTaskId);
        parentStillInProgress.Should().NotBeNull();
        parentStillInProgress!.Status.Should().Be(TaskStatus.InProgress);
    }

    [Fact]
    public async Task Full_Lifecycle_Multiple_Subtasks_All_Must_Pass()
    {
        // Arrange: seed agent and parent task
        var agentId = await SeedAgentWithCapabilitiesAsync();
        var agent = await _agentRepo.GetByIdAsync(agentId);
        var parentTaskId = await SeedParentTaskAsync();
        var parentTask = await _taskRepo.GetByIdAsync(parentTaskId);

        // Plan has 2 subtasks
        var plan = new OrchestrationPlan
        {
            Subtasks = new List<PlannedSubtask>
            {
                new PlannedSubtask
                {
                    Title = "Implement quicksort",
                    Description = "Quicksort algorithm",
                    TaskType = TaskType.Code,
                    EstimatedSats = 15_000,
                    VerificationCriteria = "Quicksort tests pass"
                },
                new PlannedSubtask
                {
                    Title = "Implement mergesort",
                    Description = "Mergesort algorithm",
                    TaskType = TaskType.Code,
                    EstimatedSats = 15_000,
                    DependsOn = new List<int> { 0 },
                    VerificationCriteria = "Mergesort tests pass"
                }
            },
            EstimatedTotalSats = 30_000,
            EstimatedTotalTimeSec = 180
        };

        _claudeAi.SendStructuredRequestAsync<OrchestrationPlan>(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(plan);

        _agentMatcher.FindBestAgentAsync(Arg.Any<TaskItem>(), Arg.Any<CancellationToken>())
            .Returns(new List<(Agent Agent, double MatchScore)> { (agent!, 0.9) });

        _spendLimitService.CheckLimitAsync(
            Arg.Any<int>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(true);

        _escrowManager.CreateEscrowAsync(Arg.Any<Milestone>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var ms = callInfo.ArgAt<Milestone>(0);
                return new Escrow
                {
                    Id = ms.Id * 10,
                    MilestoneId = ms.Id,
                    TaskId = ms.TaskId,
                    AmountSats = ms.PayoutSats,
                    PaymentHash = "hash_" + ms.Id,
                    Status = EscrowStatus.Held,
                    HodlInvoice = "lnbc...",
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddHours(1)
                };
            });

        _eventPublisher.PublishTaskAssignedAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var orchestrator = CreateOrchestrator();

        // Act: orchestrate
        await orchestrator.OrchestrateTaskAsync(parentTask!);

        // Verify 2 subtasks created
        var subtasks = await _taskRepo.GetSubtasksAsync(parentTaskId);
        subtasks.Should().HaveCount(2);

        // Mark only first milestone as Passed, second stays Pending
        var ms1 = (await _milestoneRepo.GetByTaskIdAsync(subtasks[0].Id))[0];
        ms1.Status = MilestoneStatus.Passed;
        ms1.OutputData = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("quicksort output"));
        ms1.VerifiedAt = DateTime.UtcNow;
        await _milestoneRepo.UpdateAsync(ms1);

        // Task should NOT be complete yet (second milestone still Pending)
        var notDone = await orchestrator.CheckAndCompleteTaskAsync(parentTaskId);
        notDone.Should().BeFalse();

        var parentStillInProgress = await _taskRepo.GetByIdAsync(parentTaskId);
        parentStillInProgress!.Status.Should().Be(TaskStatus.InProgress);

        // Now mark the second milestone as Passed too
        var ms2 = (await _milestoneRepo.GetByTaskIdAsync(subtasks[1].Id))[0];
        ms2.Status = MilestoneStatus.Passed;
        ms2.OutputData = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("mergesort output"));
        ms2.VerifiedAt = DateTime.UtcNow;
        await _milestoneRepo.UpdateAsync(ms2);

        // Set up assembler
        _claudeAi.SendMessageAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("Combined sorting library");

        // Now it should be complete
        var done = await orchestrator.CheckAndCompleteTaskAsync(parentTaskId);
        done.Should().BeTrue();

        var completedParent = await _taskRepo.GetByIdAsync(parentTaskId);
        completedParent!.Status.Should().Be(TaskStatus.Completed);

        // Both subtasks should be marked Completed
        var completedSubtasks = await _taskRepo.GetSubtasksAsync(parentTaskId);
        completedSubtasks.Should().AllSatisfy(s =>
            s.Status.Should().Be(TaskStatus.Completed));

        // Verify 2 escrows were created (one per milestone)
        await _escrowManager.Received(2).CreateEscrowAsync(
            Arg.Any<Milestone>(), Arg.Any<CancellationToken>());

        // Verify 2 task-assigned events were published
        await _eventPublisher.Received(2).PublishTaskAssignedAsync(
            Arg.Any<int>(), agentId, Arg.Any<CancellationToken>());
    }
}
