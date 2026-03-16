namespace LightningAgentMarketPlace.Core.Models.Lightning;

public class OpenChannelResult
{
    public string FundingTxId { get; set; } = string.Empty;
    public int OutputIndex { get; set; }
}
