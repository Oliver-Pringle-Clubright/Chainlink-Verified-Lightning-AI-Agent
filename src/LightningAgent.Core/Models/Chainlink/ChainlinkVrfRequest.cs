namespace LightningAgent.Core.Models.Chainlink;

public class ChainlinkVrfRequest
{
    public string RequestId { get; set; } = string.Empty;
    public int NumWords { get; set; }
    public List<string>? Randomness { get; set; }
}
