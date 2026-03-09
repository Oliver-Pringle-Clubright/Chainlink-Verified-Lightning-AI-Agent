namespace LightningAgent.Core.Models.Lightning;

public class InvoiceState
{
    public string PaymentHash { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public long AmountSats { get; set; }
    public DateTime? SettledAt { get; set; }
    public bool IsHeld { get; set; }
}
