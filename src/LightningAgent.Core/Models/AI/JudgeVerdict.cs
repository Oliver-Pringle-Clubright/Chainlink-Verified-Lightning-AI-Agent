namespace LightningAgent.Core.Models.AI;

public class JudgeVerdict
{
    public int MilestoneId { get; set; }
    public double Score { get; set; }
    public bool Passed { get; set; }
    public string Reasoning { get; set; } = string.Empty;
    public List<string>? Concerns { get; set; }
    public List<string>? Suggestions { get; set; }
}
