namespace LightningAgentMarketPlace.Core.Models.Chainlink;

public class CcipSupportedChain
{
    public ulong ChainSelector { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NetworkType { get; set; } = string.Empty; // Testnet or Mainnet
    public string RouterAddress { get; set; } = string.Empty;
    public string? RpcUrl { get; set; }
}
