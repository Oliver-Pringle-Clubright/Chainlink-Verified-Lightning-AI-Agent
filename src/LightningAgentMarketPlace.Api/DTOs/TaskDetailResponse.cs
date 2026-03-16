namespace LightningAgentMarketPlace.Api.DTOs;

public class TaskDetailResponse
{
    public int Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long MaxPayoutSats { get; set; }
    public long ActualPayoutSats { get; set; }
    public double? PriceUsd { get; set; }
    public int? AssignedAgentId { get; set; }
    public int Priority { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<MilestoneDto>? Milestones { get; set; }
}
