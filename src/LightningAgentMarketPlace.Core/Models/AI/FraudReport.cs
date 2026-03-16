namespace LightningAgentMarketPlace.Core.Models.AI;

public class FraudReport
{
    public string AgentId { get; set; } = string.Empty;
    public string FraudType { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public List<string> Evidence { get; set; } = new();
    public string RecommendedAction { get; set; } = string.Empty;
}
