namespace LightningAgent.Core.Models.Chainlink;

public class PriceFeedData
{
    public long RoundId { get; set; }
    public decimal Answer { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long AnsweredInRound { get; set; }
}
