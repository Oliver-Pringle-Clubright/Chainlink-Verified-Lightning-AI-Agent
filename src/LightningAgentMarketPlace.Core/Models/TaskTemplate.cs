namespace LightningAgentMarketPlace.Core.Models;

public class TaskTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string TaskType { get; set; } = "Code";
    public string VerificationCriteria { get; set; } = "";
    public long SuggestedPayoutSats { get; set; }
    public string RequiredSkills { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
