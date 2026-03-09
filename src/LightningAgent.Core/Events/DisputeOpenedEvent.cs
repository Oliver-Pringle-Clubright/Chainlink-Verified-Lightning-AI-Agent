namespace LightningAgent.Core.Events;

public record DisputeOpenedEvent(int DisputeId, int TaskId, string Reason, DateTime Timestamp);
