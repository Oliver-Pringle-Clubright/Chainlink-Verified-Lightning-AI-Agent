using LightningAgent.Core.Enums;

namespace LightningAgent.Core.Models;

public class Escrow
{
    public int Id { get; set; }
    public int MilestoneId { get; set; }
    public int TaskId { get; set; }
    public long AmountSats { get; set; }
    public string PaymentHash { get; set; } = string.Empty;
    public string? PaymentPreimage { get; set; }
    public EscrowStatus Status { get; set; }
    public string HodlInvoice { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? SettledAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
