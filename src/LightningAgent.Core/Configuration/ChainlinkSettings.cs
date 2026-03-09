namespace LightningAgent.Core.Configuration;

public class ChainlinkSettings
{
    public string EthereumRpcUrl { get; set; } = "";
    public string FunctionsRouterAddress { get; set; } = "";
    public string AutomationRegistryAddress { get; set; } = "";
    public string VrfCoordinatorAddress { get; set; } = "";
    public string BtcUsdPriceFeedAddress { get; set; } = "";
    public long SubscriptionId { get; set; } = 0;
    public string DonId { get; set; } = "";
    public string PrivateKeyPath { get; set; } = "";
}
