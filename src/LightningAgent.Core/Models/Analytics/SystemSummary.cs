namespace LightningAgent.Core.Models.Analytics;

public class SystemSummary
{
    public int TotalTasks { get; set; }
    public int PendingTasks { get; set; }
    public int InProgressTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int FailedTasks { get; set; }
    public int DisputedTasks { get; set; }
    public double AvgCompletionTimeSec { get; set; }
    public long TotalSatsSpent { get; set; }
    public double TotalUsdSpent { get; set; }
    public int TotalAgents { get; set; }
    public int ActiveAgents { get; set; }
    public long HeldEscrowSats { get; set; }
    public DateTime GeneratedAt { get; set; }
}
