namespace LightningAgent.Api.DTOs;

public class AgentCapabilityDto
{
    public string SkillType { get; set; } = string.Empty;
    public List<string> TaskTypes { get; set; } = new();
    public int? MaxConcurrency { get; set; }
    public long PriceSatsPerUnit { get; set; }
}
