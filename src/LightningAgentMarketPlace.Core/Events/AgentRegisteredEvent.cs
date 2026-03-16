namespace LightningAgentMarketPlace.Core.Events;

public record AgentRegisteredEvent(
    int AgentId,
    string Name,
    DateTime OccurredAt);
