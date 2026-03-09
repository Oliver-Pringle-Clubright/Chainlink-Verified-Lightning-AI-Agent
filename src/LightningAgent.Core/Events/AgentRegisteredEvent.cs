namespace LightningAgent.Core.Events;

public record AgentRegisteredEvent(int AgentId, string Name, DateTime Timestamp);
