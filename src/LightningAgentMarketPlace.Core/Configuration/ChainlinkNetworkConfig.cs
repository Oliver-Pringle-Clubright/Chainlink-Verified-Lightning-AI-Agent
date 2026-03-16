namespace LightningAgentMarketPlace.Core.Configuration;

/// <summary>
/// Network-specific Chainlink contract addresses and RPC configuration.
/// Used as Testnet/Mainnet sub-objects within ChainlinkSettings.
/// </summary>
public class ChainlinkNetworkConfig
{
    public string EthereumRpcUrl { get; set; } = "";
    public string FunctionsRouterAddress { get; set; } = "";
    public string AutomationRegistryAddress { get; set; } = "";
    public string VrfCoordinatorAddress { get; set; } = "";
    public string VrfKeyHash { get; set; } = "";
    public string VrfConsumerAddress { get; set; } = "";
    public string BtcUsdPriceFeedAddress { get; set; } = "";
    public string EthUsdPriceFeedAddress { get; set; } = "";
    public string LinkUsdPriceFeedAddress { get; set; } = "";
    public string LinkEthPriceFeedAddress { get; set; } = "";
    public string VrfSubscriptionId { get; set; } = "";
    public string FunctionsSubscriptionId { get; set; } = "";
    public string DonId { get; set; } = "";
    public string PrivateKeyPath { get; set; } = "";
    public string CcipRouterAddress { get; set; } = "";
    public ulong CcipSourceChainSelector { get; set; }

    // Deployed contract addresses (VerifiedEscrow, FairAssignment, ReputationLedger, DeadlineEnforcer)
    public string VerifiedEscrowAddress { get; set; } = "";
    public string FairAssignmentAddress { get; set; } = "";
    public string ReputationLedgerAddress { get; set; } = "";
    public string DeadlineEnforcerAddress { get; set; } = "";
}
