namespace LightningAgent.Core.Models.Acp;

public class AcpTaskSpec
{
    public string TaskId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public List<string> RequiredSkills { get; set; } = new();
    public AcpBudget Budget { get; set; } = new();
    public string? VerificationRequirements { get; set; }
    public DateTime? Deadline { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

public class AcpBudget
{
    public long MaxSats { get; set; }
    public string PreferredCurrency { get; set; } = string.Empty;
    public double? UsdEquivalent { get; set; }
}
