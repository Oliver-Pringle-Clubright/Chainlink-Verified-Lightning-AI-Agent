namespace LightningAgentMarketPlace.Core.Models.Chainlink;

public class CcipFeeEstimate
{
    public ulong DestinationChainSelector { get; set; }
    public string FeeToken { get; set; } = string.Empty;
    public string FeeAmountWei { get; set; } = "0";
    public double FeeUsd { get; set; }
    public DateTime EstimatedAt { get; set; }
}
