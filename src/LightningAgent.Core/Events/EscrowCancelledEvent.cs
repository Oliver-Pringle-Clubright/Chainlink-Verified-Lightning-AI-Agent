namespace LightningAgent.Core.Events;

public record EscrowCancelledEvent(
    int EscrowId,
    int MilestoneId,
    long AmountSats,
    string Reason,
    DateTime OccurredAt);
