namespace LightningAgentMarketPlace.Core.Models.Lightning;

public class LndInfo
{
    public string? Alias { get; set; }
    public string PubKey { get; set; } = string.Empty;
    public int NumActiveChannels { get; set; }
    public int NumPeers { get; set; }
    public int BlockHeight { get; set; }
    public bool SyncedToChain { get; set; }
}
