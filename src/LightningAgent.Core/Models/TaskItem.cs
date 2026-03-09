using LightningAgent.Core.Enums;
using TaskStatus = LightningAgent.Core.Enums.TaskStatus;

namespace LightningAgent.Core.Models;

public class TaskItem
{
    public int Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public int? ParentTaskId { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TaskType TaskType { get; set; }
    public TaskStatus Status { get; set; }
    public string? AcpSpec { get; set; }
    public string? VerificationCriteria { get; set; }
    public long MaxPayoutSats { get; set; }
    public long ActualPayoutSats { get; set; }
    public double? PriceUsd { get; set; }
    public int? AssignedAgentId { get; set; }
    public int Priority { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
