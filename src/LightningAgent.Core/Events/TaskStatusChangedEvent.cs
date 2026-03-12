namespace LightningAgent.Core.Events;

public record TaskStatusChangedEvent(
    int TaskId,
    string PreviousStatus,
    string NewStatus,
    int? AgentId,
    DateTime OccurredAt);
