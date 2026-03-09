using LightningAgent.Core.Enums;

namespace LightningAgent.Core.Models;

public class AgentCapability
{
    public int Id { get; set; }
    public int AgentId { get; set; }
    public SkillType SkillType { get; set; }
    public string TaskTypes { get; set; } = string.Empty;
    public int MaxConcurrency { get; set; }
    public long PriceSatsPerUnit { get; set; }
    public int? AvgResponseSec { get; set; }
    public DateTime CreatedAt { get; set; }
}
