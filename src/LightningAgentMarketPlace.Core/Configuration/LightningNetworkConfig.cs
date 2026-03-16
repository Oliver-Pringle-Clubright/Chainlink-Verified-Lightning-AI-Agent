namespace LightningAgentMarketPlace.Core.Configuration;

/// <summary>
/// Network-specific Lightning node configuration.
/// Used as Testnet/Mainnet sub-objects within LightningSettings.
/// </summary>
public class LightningNetworkConfig
{
    public string LndRestUrl { get; set; } = "";
    public string MacaroonPath { get; set; } = "";
    public string TlsCertPath { get; set; } = "";
}
