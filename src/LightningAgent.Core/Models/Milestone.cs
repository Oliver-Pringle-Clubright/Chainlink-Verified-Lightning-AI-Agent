using LightningAgent.Core.Enums;

namespace LightningAgent.Core.Models;

public class Milestone
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public int SequenceNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string VerificationCriteria { get; set; } = string.Empty;
    public long PayoutSats { get; set; }
    public MilestoneStatus Status { get; set; }
    public string? VerificationResult { get; set; }
    public string? InvoicePaymentHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime? PaidAt { get; set; }
}
