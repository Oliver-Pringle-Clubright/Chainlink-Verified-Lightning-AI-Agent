namespace LightningAgentMarketPlace.Core.Events;

public record VerificationFailedEvent(
    int MilestoneId,
    int TaskId,
    string Reason,
    DateTime OccurredAt);
