namespace LightningAgentMarketPlace.Core.Models.Lightning;

public class MultiPathPaymentResult
{
    public string PaymentPreimage { get; set; } = string.Empty;
    public string PaymentHash { get; set; } = string.Empty;
    public long FeeSats { get; set; }
    public long AmountSats { get; set; }
    public string Status { get; set; } = string.Empty;
    public int NumParts { get; set; }
    public List<string> Hops { get; set; } = new();
}
