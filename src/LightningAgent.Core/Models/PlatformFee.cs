namespace LightningAgent.Core.Models;

public class PlatformFee
{
    public int Id { get; set; }
    public string FeeType { get; set; } = "";   // Commission, TaskPosting, Verification
    public int? TaskId { get; set; }
    public int? MilestoneId { get; set; }
    public int? PaymentId { get; set; }
    public long AmountSats { get; set; }
    public DateTime CreatedAt { get; set; }
}
