namespace LightningAgent.Acp.AcpProtocolModels;

/// <summary>
/// Wire format for posting a task to an ACP-compatible endpoint.
/// </summary>
public class AcpTaskPosting
{
    public string TaskId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public List<string> RequiredSkills { get; set; } = new();
    public long MaxBudgetSats { get; set; }
    public DateTime? Deadline { get; set; }
}
