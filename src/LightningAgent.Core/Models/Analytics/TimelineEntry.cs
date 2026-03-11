namespace LightningAgent.Core.Models.Analytics;

public class TimelineEntry
{
    public string Date { get; set; } = string.Empty;
    public int Created { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }
    public long SatsSpent { get; set; }
}
