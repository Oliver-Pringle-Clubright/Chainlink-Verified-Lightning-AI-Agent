namespace LightningAgentMarketPlace.Core.Models;

public class PriceQuote
{
    public int Id { get; set; }
    public string Pair { get; set; } = string.Empty;
    public double PriceUsd { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTime FetchedAt { get; set; }
}
