namespace LightningAgentMarketPlace.Core.Models.Chainlink;

public class ChainlinkAutomationUpkeep
{
    public string UpkeepId { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string? CheckData { get; set; }
    public int GasLimit { get; set; }
    public bool IsActive { get; set; }
}
