namespace LightningAgentMarketPlace.Core.Models.Lightning;

public class ChannelBalance
{
    public long LocalBalanceSats { get; set; }
    public long RemoteBalanceSats { get; set; }
    public long UnsettledLocalBalanceSats { get; set; }
    public long UnsettledRemoteBalanceSats { get; set; }
    public long PendingOpenLocalBalanceSats { get; set; }
    public long PendingOpenRemoteBalanceSats { get; set; }
}
