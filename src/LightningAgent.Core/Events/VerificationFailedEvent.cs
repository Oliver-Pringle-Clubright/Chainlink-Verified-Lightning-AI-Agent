namespace LightningAgent.Core.Events;

public record VerificationFailedEvent(int MilestoneId, int TaskId, string Reason, DateTime Timestamp);
