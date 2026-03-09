namespace LightningAgent.Core.Events;

public record EscrowSettledEvent(
    int EscrowId,
    int MilestoneId,
    long AmountSats,
    DateTime OccurredAt);
