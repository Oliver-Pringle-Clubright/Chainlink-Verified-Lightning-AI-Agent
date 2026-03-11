using LightningAgent.Core.Models.Analytics;

namespace LightningAgent.Core.Interfaces.Data;

public interface IAnalyticsRepository
{
    Task<SystemSummary> GetSystemSummaryAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AgentStats>> GetAgentStatsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TimelineEntry>> GetTimelineAsync(int days, CancellationToken ct = default);
}
