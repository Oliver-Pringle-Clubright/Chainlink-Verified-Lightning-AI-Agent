using LightningAgent.Core.Enums;

namespace LightningAgent.Core.Models;

public class Payment
{
    public int Id { get; set; }
    public int? EscrowId { get; set; }
    public int TaskId { get; set; }
    public int? MilestoneId { get; set; }
    public int AgentId { get; set; }
    public long AmountSats { get; set; }
    public double? AmountUsd { get; set; }
    public string? PaymentHash { get; set; }
    public PaymentType PaymentType { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public PaymentStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SettledAt { get; set; }

    // Multi-chain payment fields
    public long? ChainId { get; set; }
    public string? TokenAddress { get; set; }
    public string? TransactionHash { get; set; }
    public string? SenderAddress { get; set; }
    public string? ReceiverAddress { get; set; }
    public string? AmountWei { get; set; }
}
