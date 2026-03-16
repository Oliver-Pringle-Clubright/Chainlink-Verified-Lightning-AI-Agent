namespace LightningAgentMarketPlace.Core.Models.Chainlink;

public class PriceFeedData
{
    public string RoundId { get; set; } = "";
    public decimal Answer { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string AnsweredInRound { get; set; } = "";
}
