namespace LightningAgent.Api.DTOs;

public class OpenDisputeRequest
{
    public int TaskId { get; set; }
    public int? MilestoneId { get; set; }
    public string InitiatedBy { get; set; } = string.Empty;
    public string InitiatorId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public long AmountDisputedSats { get; set; }
}
