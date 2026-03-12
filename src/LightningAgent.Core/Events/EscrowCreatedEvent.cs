namespace LightningAgent.Core.Events;

public record EscrowCreatedEvent(
    int EscrowId,
    int MilestoneId,
    long AmountSats,
    DateTime OccurredAt);
