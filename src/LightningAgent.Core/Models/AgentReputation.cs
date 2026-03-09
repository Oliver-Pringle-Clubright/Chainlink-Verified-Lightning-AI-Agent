namespace LightningAgent.Core.Models;

public class AgentReputation
{
    public int Id { get; set; }
    public int AgentId { get; set; }
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int VerificationPasses { get; set; }
    public int VerificationFails { get; set; }
    public int DisputeCount { get; set; }
    public double AvgResponseTimeSec { get; set; }
    public double ReputationScore { get; set; }
    public DateTime LastUpdated { get; set; }
}
