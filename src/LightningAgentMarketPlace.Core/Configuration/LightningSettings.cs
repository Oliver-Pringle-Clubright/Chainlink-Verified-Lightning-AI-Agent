namespace LightningAgentMarketPlace.Core.Configuration;

public class LightningSettings
{
    public string LndRestUrl { get; set; } = "https://localhost:8080";
    public string MacaroonPath { get; set; } = "";
    public string TlsCertPath { get; set; } = "";
    public int DefaultInvoiceExpirySec { get; set; } = 3600;

    // Network-specific configurations
    public LightningNetworkConfig Testnet { get; set; } = new();
    public LightningNetworkConfig Mainnet { get; set; } = new();
}
