namespace LightningAgentMarketPlace.Api.DTOs;

public class PriceEstimateResponse
{
    public long EstimatedSats { get; set; }
    public double EstimatedUsd { get; set; }
    public double BtcUsdRate { get; set; }
}
