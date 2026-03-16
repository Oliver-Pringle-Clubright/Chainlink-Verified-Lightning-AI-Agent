namespace LightningAgentMarketPlace.Core.Configuration;

public class PricingSettings
{
    public double MarginMultiplier { get; set; } = 1.05;
    public long MinPriceSats { get; set; } = 10;
    public long MaxPriceSats { get; set; } = 10000000;
}
