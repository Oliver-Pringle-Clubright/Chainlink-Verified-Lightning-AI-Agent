namespace LightningAgent.Core.Models;

public class SpendLimit
{
    public int Id { get; set; }
    public int? AgentId { get; set; }
    public int? TaskId { get; set; }
    public string LimitType { get; set; } = string.Empty;
    public long MaxSats { get; set; }
    public long CurrentSpentSats { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
}
