namespace LightningAgent.Core.Models.Lightning;

public class PaymentRoute
{
    public long TotalAmtSats { get; set; }
    public long TotalFeesSats { get; set; }
    public List<string> Hops { get; set; } = new();
}
