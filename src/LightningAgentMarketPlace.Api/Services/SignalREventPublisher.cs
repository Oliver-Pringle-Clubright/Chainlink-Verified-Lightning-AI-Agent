namespace LightningAgentMarketPlace.Api.Services;

using Microsoft.AspNetCore.SignalR;
using LightningAgentMarketPlace.Api.Hubs;
using LightningAgentMarketPlace.Core.Events;
using LightningAgentMarketPlace.Core.Interfaces.Data;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using LightningAgentMarketPlace.Engine;

public class SignalREventPublisher : IEventPublisher
{
    private readonly IHubContext<AgentNotificationHub> _hub;
    private readonly WebhookDeliveryService _webhookDelivery;
    private readonly ITaskRepository _taskRepo;
    private readonly ILogger<SignalREventPublisher> _logger;

    public SignalREventPublisher(
        IHubContext<AgentNotificationHub> hub,
        WebhookDeliveryService webhookDelivery,
        ITaskRepository taskRepo,
        ILogger<SignalREventPublisher> logger)
    {
        _hub = hub;
        _webhookDelivery = webhookDelivery;
        _taskRepo = taskRepo;
        _logger = logger;
    }

    public async Task PublishTaskStatusChangedAsync(int taskId, string previousStatus, string newStatus, int? agentId = null, CancellationToken ct = default)
    {
        var evt = new Core.Events.TaskStatusChangedEvent(taskId, previousStatus, newStatus, agentId, DateTime.UtcNow);
        _logger.LogInformation(
            "Publishing TaskStatusChanged event: Task {TaskId} {Previous} -> {New}",
            taskId, previousStatus, newStatus);

        await _hub.Clients.All.SendAsync("TaskStatusChanged", evt, ct);
        await _hub.Clients.Group($"task-{taskId}").SendAsync("TaskStatusChanged", evt, ct);
        if (agentId.HasValue)
        {
            await _hub.Clients.Group($"agent-{agentId}").SendAsync("TaskStatusChanged", evt, ct);
        }
    }

    public async Task PublishEscrowCreatedAsync(int escrowId, int milestoneId, long amountSats, CancellationToken ct = default)
    {
        var evt = new Core.Events.EscrowCreatedEvent(escrowId, milestoneId, amountSats, DateTime.UtcNow);
        _logger.LogInformation(
            "Publishing EscrowCreated event: Escrow {EscrowId} Milestone {MilestoneId} Amount {AmountSats} sats",
            escrowId, milestoneId, amountSats);

        await _hub.Clients.All.SendAsync("EscrowCreated", evt, ct);
    }

    public async Task PublishEscrowCancelledAsync(int escrowId, int milestoneId, long amountSats, string reason, CancellationToken ct = default)
    {
        var evt = new Core.Events.EscrowCancelledEvent(escrowId, milestoneId, amountSats, reason, DateTime.UtcNow);
        _logger.LogInformation(
            "Publishing EscrowCancelled event: Escrow {EscrowId} Milestone {MilestoneId} Reason: {Reason}",
            escrowId, milestoneId, reason);

        await _hub.Clients.All.SendAsync("EscrowCancelled", evt, ct);
    }

    public async Task PublishTaskAssignedAsync(int taskId, int agentId, CancellationToken ct = default)
    {
        var evt = new TaskAssignedEvent(taskId, agentId, DateTime.UtcNow);
        _logger.LogInformation("Publishing TaskAssigned event: Task {TaskId} -> Agent {AgentId}", taskId, agentId);

        await _hub.Clients.All.SendAsync("TaskAssigned", evt, ct);
        await _hub.Clients.Group($"task-{taskId}").SendAsync("TaskAssigned", evt, ct);
        await _hub.Clients.Group($"agent-{agentId}").SendAsync("TaskAssigned", evt, ct);
        await _webhookDelivery.DeliverAsync(agentId, "TaskAssigned", evt, ct);
    }

    public async Task PublishMilestoneVerifiedAsync(int milestoneId, int taskId, bool passed, double score, CancellationToken ct = default)
    {
        var evt = new MilestoneVerifiedEvent(milestoneId, taskId, passed, score, DateTime.UtcNow);
        _logger.LogInformation(
            "Publishing MilestoneVerified event: Milestone {MilestoneId} Task {TaskId} Passed={Passed} Score={Score:F3}",
            milestoneId, taskId, passed, score);

        await _hub.Clients.All.SendAsync("MilestoneVerified", evt, ct);
        await _hub.Clients.Group($"task-{taskId}").SendAsync("MilestoneVerified", evt, ct);

        var task = await _taskRepo.GetByIdAsync(taskId, ct);
        if (task?.AssignedAgentId is int milestoneAgentId)
        {
            await _hub.Clients.Group($"agent-{milestoneAgentId}").SendAsync("MilestoneVerified", evt, ct);
            await _webhookDelivery.DeliverAsync(milestoneAgentId, "MilestoneVerified", evt, ct);
        }
    }

    public async Task PublishPaymentSentAsync(int paymentId, int agentId, long amountSats, CancellationToken ct = default)
    {
        var evt = new PaymentSentEvent(paymentId, agentId, amountSats, DateTime.UtcNow);
        _logger.LogInformation(
            "Publishing PaymentSent event: Payment {PaymentId} Agent {AgentId} Amount {AmountSats} sats",
            paymentId, agentId, amountSats);

        await _hub.Clients.All.SendAsync("PaymentSent", evt, ct);
        await _hub.Clients.Group($"agent-{agentId}").SendAsync("PaymentSent", evt, ct);
        await _webhookDelivery.DeliverAsync(agentId, "PaymentSent", evt, ct);
    }

    public async Task PublishDisputeOpenedAsync(int disputeId, int taskId, string reason, CancellationToken ct = default)
    {
        var evt = new DisputeOpenedEvent(disputeId, taskId, reason, DateTime.UtcNow);
        _logger.LogInformation(
            "Publishing DisputeOpened event: Dispute {DisputeId} Task {TaskId}",
            disputeId, taskId);

        await _hub.Clients.All.SendAsync("DisputeOpened", evt, ct);
        await _hub.Clients.Group($"task-{taskId}").SendAsync("DisputeOpened", evt, ct);

        var disputeTask = await _taskRepo.GetByIdAsync(taskId, ct);
        if (disputeTask?.AssignedAgentId is int disputeAgentId)
        {
            await _hub.Clients.Group($"agent-{disputeAgentId}").SendAsync("DisputeOpened", evt, ct);
            await _webhookDelivery.DeliverAsync(disputeAgentId, "DisputeOpened", evt, ct);
        }
    }

    public async Task PublishEscrowSettledAsync(int escrowId, int milestoneId, long amountSats, CancellationToken ct = default)
    {
        var evt = new EscrowSettledEvent(escrowId, milestoneId, amountSats, DateTime.UtcNow);
        _logger.LogInformation(
            "Publishing EscrowSettled event: Escrow {EscrowId} Milestone {MilestoneId} Amount {AmountSats} sats",
            escrowId, milestoneId, amountSats);

        await _hub.Clients.All.SendAsync("EscrowSettled", evt, ct);
    }

    public async Task PublishVerificationFailedAsync(int milestoneId, int taskId, string reason, CancellationToken ct = default)
    {
        var evt = new VerificationFailedEvent(milestoneId, taskId, reason, DateTime.UtcNow);
        _logger.LogInformation(
            "Publishing VerificationFailed event: Milestone {MilestoneId} Task {TaskId}",
            milestoneId, taskId);

        await _hub.Clients.All.SendAsync("VerificationFailed", evt, ct);
        await _hub.Clients.Group($"task-{taskId}").SendAsync("VerificationFailed", evt, ct);

        var failedTask = await _taskRepo.GetByIdAsync(taskId, ct);
        if (failedTask?.AssignedAgentId is int failedAgentId)
        {
            await _hub.Clients.Group($"agent-{failedAgentId}").SendAsync("VerificationFailed", evt, ct);
            await _webhookDelivery.DeliverAsync(failedAgentId, "VerificationFailed", evt, ct);
        }
    }

    public async Task PublishAgentRegisteredAsync(int agentId, string name, CancellationToken ct = default)
    {
        var evt = new AgentRegisteredEvent(agentId, name, DateTime.UtcNow);
        _logger.LogInformation(
            "Publishing AgentRegistered event: Agent {AgentId} Name {Name}",
            agentId, name);

        await _hub.Clients.All.SendAsync("AgentRegistered", evt, ct);
        await _hub.Clients.Group($"agent-{agentId}").SendAsync("AgentRegistered", evt, ct);
        await _webhookDelivery.DeliverAsync(agentId, "AgentRegistered", evt, ct);
    }
}
