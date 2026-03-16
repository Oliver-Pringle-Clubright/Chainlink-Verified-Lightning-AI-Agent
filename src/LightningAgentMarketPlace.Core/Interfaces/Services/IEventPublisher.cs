namespace LightningAgentMarketPlace.Core.Interfaces.Services;

public interface IEventPublisher
{
    Task PublishTaskAssignedAsync(int taskId, int agentId, CancellationToken ct = default);
    Task PublishTaskStatusChangedAsync(int taskId, string previousStatus, string newStatus, int? agentId = null, CancellationToken ct = default);
    Task PublishMilestoneVerifiedAsync(int milestoneId, int taskId, bool passed, double score, CancellationToken ct = default);
    Task PublishPaymentSentAsync(int paymentId, int agentId, long amountSats, CancellationToken ct = default);
    Task PublishDisputeOpenedAsync(int disputeId, int taskId, string reason, CancellationToken ct = default);
    Task PublishEscrowCreatedAsync(int escrowId, int milestoneId, long amountSats, CancellationToken ct = default);
    Task PublishEscrowSettledAsync(int escrowId, int milestoneId, long amountSats, CancellationToken ct = default);
    Task PublishEscrowCancelledAsync(int escrowId, int milestoneId, long amountSats, string reason, CancellationToken ct = default);
    Task PublishVerificationFailedAsync(int milestoneId, int taskId, string reason, CancellationToken ct = default);
    Task PublishAgentRegisteredAsync(int agentId, string name, CancellationToken ct = default);
}
