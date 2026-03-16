using System.Text.Json.Serialization;

namespace LightningAgentMarketPlace.Lightning.LndApiModels;

internal class CancelInvoiceRequest
{
    [JsonPropertyName("payment_hash")]
    public string PaymentHash { get; set; } = string.Empty;
}
