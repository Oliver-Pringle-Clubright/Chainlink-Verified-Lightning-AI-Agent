namespace LightningAgentMarketPlace.Core.Models.Lightning;

public class LndChannel
{
    public bool Active { get; set; }
    public string RemotePubkey { get; set; } = string.Empty;
    public string ChannelPoint { get; set; } = string.Empty;
    public long Capacity { get; set; }
    public long LocalBalance { get; set; }
    public long RemoteBalance { get; set; }
    public long TotalSatoshisSent { get; set; }
    public long TotalSatoshisReceived { get; set; }
    public long NumUpdates { get; set; }
    public long ChanId { get; set; }
}
