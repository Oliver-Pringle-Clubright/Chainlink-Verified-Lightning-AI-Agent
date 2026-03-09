namespace LightningAgent.Acp.AcpProtocolModels;

/// <summary>
/// Wire format for an agent's bid response to a posted ACP task.
/// </summary>
public class AcpBidResponse
{
    public string BidId { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public long PriceSats { get; set; }
    public int EstimatedCompletionSec { get; set; }
    public bool Accepted { get; set; }
}
