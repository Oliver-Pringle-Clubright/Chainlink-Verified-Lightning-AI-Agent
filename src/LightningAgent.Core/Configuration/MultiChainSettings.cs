namespace LightningAgent.Core.Configuration;

/// <summary>
/// Configuration for secondary chain RPC connections.
/// The primary chain is always configured via ChainlinkSettings.EthereumRpcUrl.
/// Secondary chains enable reading price feeds and sending CCIP messages across chains.
/// </summary>
public class MultiChainSettings
{
    public bool Enabled { get; set; }
    public Dictionary<string, ChainRpcConfig> Chains { get; set; } = new();
}

public class ChainRpcConfig
{
    public long ChainId { get; set; }
    public string RpcUrl { get; set; } = "";
}
