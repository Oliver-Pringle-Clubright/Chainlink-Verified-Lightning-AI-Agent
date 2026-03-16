namespace LightningAgentMarketPlace.Core.Models.Acp;

public class AcpAgentOffer
{
    public string OfferId { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public long PriceSats { get; set; }
    public int EstimatedCompletionSec { get; set; }
    public string? Message { get; set; }
    public List<string> Capabilities { get; set; } = new();
}
