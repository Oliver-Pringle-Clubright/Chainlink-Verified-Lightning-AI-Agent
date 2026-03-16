namespace LightningAgentMarketPlace.Core.Models.Acp;

public class AcpNegotiationMessage
{
    public string MessageId { get; set; } = string.Empty;
    public string FromAgentId { get; set; } = string.Empty;
    public string ToAgentId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public long ProposedPriceSats { get; set; }
    public string? CounterTerms { get; set; }
    public bool IsAccepted { get; set; }
    public DateTime Timestamp { get; set; }
}
