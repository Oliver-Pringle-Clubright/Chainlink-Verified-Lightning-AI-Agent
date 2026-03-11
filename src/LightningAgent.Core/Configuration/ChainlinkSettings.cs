namespace LightningAgent.Core.Configuration;

public class ChainlinkSettings
{
    public string EthereumRpcUrl { get; set; } = "";
    public string FunctionsRouterAddress { get; set; } = "";
    public string AutomationRegistryAddress { get; set; } = "";
    public string VrfCoordinatorAddress { get; set; } = "";
    public string BtcUsdPriceFeedAddress { get; set; } = "";
    public string FunctionsSubscriptionId { get; set; } = "";
    public string DonId { get; set; } = "";
    public string PrivateKeyPath { get; set; } = "";

    // VRF configuration
    public string VrfKeyHash { get; set; } = "";
    public string VrfConsumerAddress { get; set; } = "";
    public string VrfSubscriptionId { get; set; } = "";

    // Additional price feeds
    public string EthUsdPriceFeedAddress { get; set; } = "";
    public string LinkUsdPriceFeedAddress { get; set; } = "";
    public string LinkEthPriceFeedAddress { get; set; } = "";

    // CCIP (Cross-Chain Interoperability Protocol)
    public string CcipRouterAddress { get; set; } = "";
    public ulong CcipSourceChainSelector { get; set; }
}
