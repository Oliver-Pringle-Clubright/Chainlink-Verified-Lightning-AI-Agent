namespace LightningAgent.Api.DTOs;

public class MilestoneDto
{
    public int Id { get; set; }
    public int SequenceNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long PayoutSats { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime? PaidAt { get; set; }
}
