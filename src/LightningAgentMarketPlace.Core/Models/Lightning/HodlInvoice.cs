namespace LightningAgentMarketPlace.Core.Models.Lightning;

public class HodlInvoice
{
    public string PaymentHash { get; set; } = string.Empty;
    public string PaymentRequest { get; set; } = string.Empty;
    public long AmountSats { get; set; }
    public string? Memo { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string State { get; set; } = string.Empty;
}
