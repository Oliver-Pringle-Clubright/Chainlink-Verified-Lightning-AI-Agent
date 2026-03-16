namespace LightningAgentMarketPlace.Core.Events;

public record MilestoneVerifiedEvent(
    int MilestoneId,
    int TaskId,
    bool Passed,
    double Score,
    DateTime OccurredAt);
