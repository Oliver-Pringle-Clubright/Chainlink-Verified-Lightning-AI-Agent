using LightningAgentMarketPlace.Core.Enums;

namespace LightningAgentMarketPlace.Core.Models;

public class Dispute
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public int? MilestoneId { get; set; }
    public string InitiatedBy { get; set; } = string.Empty;
    public string InitiatorId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DisputeStatus Status { get; set; }
    public string? Resolution { get; set; }
    public int? ArbiterAgentId { get; set; }
    public long AmountDisputedSats { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
