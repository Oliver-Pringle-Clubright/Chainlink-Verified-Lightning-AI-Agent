namespace LightningAgentMarketPlace.Core.Models.Metrics;

public class AppMetrics
{
    // Counters
    public long TasksCreated { get; set; }
    public long TasksCompleted { get; set; }
    public long TasksFailed { get; set; }
    public long MilestonesVerified { get; set; }
    public long MilestonesFailed { get; set; }
    public long PaymentsProcessed { get; set; }
    public long TotalSatsPaid { get; set; }
    public long ApiRequestsTotal { get; set; }
    public long ApiErrors5xx { get; set; }

    // Gauges
    public int ActiveTasks { get; set; }
    public int ActiveAgents { get; set; }
    public int QueueDepth { get; set; }
    public double BtcUsdPrice { get; set; }

    // Histograms (simplified)
    public double AvgRequestDurationMs { get; set; }
    public double AvgVerificationDurationMs { get; set; }

    public DateTime CollectedAt { get; set; }
}
