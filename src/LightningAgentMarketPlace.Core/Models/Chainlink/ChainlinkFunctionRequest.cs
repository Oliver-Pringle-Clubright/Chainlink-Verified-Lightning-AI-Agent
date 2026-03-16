namespace LightningAgentMarketPlace.Core.Models.Chainlink;

public class ChainlinkFunctionRequest
{
    public string Source { get; set; } = string.Empty;
    public List<string>? Args { get; set; }
    public string SubscriptionId { get; set; } = string.Empty;
    public string DonId { get; set; } = string.Empty;
    public int CallbackGasLimit { get; set; }
}
