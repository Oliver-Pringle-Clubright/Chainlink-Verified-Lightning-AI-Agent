using LightningAgent.Core.Enums;

namespace LightningAgent.Core.Models.AI;

public class OrchestrationPlan
{
    public string OriginalTaskId { get; set; } = string.Empty;
    public List<PlannedSubtask> Subtasks { get; set; } = new();
    public long EstimatedTotalSats { get; set; }
    public int EstimatedTotalTimeSec { get; set; }
}

public class PlannedSubtask
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TaskType TaskType { get; set; }
    public List<string> RequiredSkills { get; set; } = new();
    public long EstimatedSats { get; set; }
    public List<int>? DependsOn { get; set; }
    public string VerificationCriteria { get; set; } = string.Empty;
}
