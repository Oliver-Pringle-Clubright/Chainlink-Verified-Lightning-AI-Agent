namespace LightningAgentMarketPlace.Core.Models.AI;

public class NegotiationProposal
{
    public string TaskId { get; set; } = string.Empty;
    public long ProposedPriceSats { get; set; }
    public string Justification { get; set; } = string.Empty;
    public Dictionary<string, string>? CounterTerms { get; set; }
    public bool ShouldAccept { get; set; }
}
