namespace LightningAgent.Core.Models.Analytics;

public class AgentStats
{
    public int AgentId { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int TasksCompleted { get; set; }
    public int TasksFailed { get; set; }
    public int TotalTasks { get; set; }
    public double AvgVerificationScore { get; set; }
    public long TotalEarnedSats { get; set; }
    public double ReputationScore { get; set; }
}
