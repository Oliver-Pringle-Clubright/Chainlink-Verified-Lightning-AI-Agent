namespace LightningAgentMarketPlace.Core.Models.Chainlink;

public class CcipMessage
{
    public int Id { get; set; }
    public string MessageId { get; set; } = string.Empty;
    public ulong SourceChainSelector { get; set; }
    public ulong DestinationChainSelector { get; set; }
    public string SenderAddress { get; set; } = string.Empty;
    public string ReceiverAddress { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string? TokenAddress { get; set; }
    public long TokenAmountWei { get; set; }
    public string FeeToken { get; set; } = string.Empty;
    public string Direction { get; set; } = "Outbound"; // Outbound or Inbound
    public string Status { get; set; } = "Pending"; // Pending, Sent, Delivered, Failed
    public string? TxHash { get; set; }
    public string? ErrorDetails { get; set; }
    public int? TaskId { get; set; }
    public int? AgentId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
}
