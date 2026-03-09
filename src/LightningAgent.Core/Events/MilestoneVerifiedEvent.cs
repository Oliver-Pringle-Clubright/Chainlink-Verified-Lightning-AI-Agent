namespace LightningAgent.Core.Events;

public record MilestoneVerifiedEvent(int MilestoneId, int TaskId, bool Passed, double Score, DateTime Timestamp);
