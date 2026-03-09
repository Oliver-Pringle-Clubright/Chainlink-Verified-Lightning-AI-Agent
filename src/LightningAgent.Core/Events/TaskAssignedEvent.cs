namespace LightningAgent.Core.Events;

public record TaskAssignedEvent(
    int TaskId,
    int AgentId,
    DateTime OccurredAt);
