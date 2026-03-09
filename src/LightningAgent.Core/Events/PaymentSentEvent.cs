namespace LightningAgent.Core.Events;

public record PaymentSentEvent(
    int PaymentId,
    int AgentId,
    long AmountSats,
    DateTime OccurredAt);
