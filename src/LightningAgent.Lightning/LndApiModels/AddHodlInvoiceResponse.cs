using System.Text.Json.Serialization;

namespace LightningAgent.Lightning.LndApiModels;

internal class AddHodlInvoiceResponse
{
    [JsonPropertyName("payment_request")]
    public string PaymentRequest { get; set; } = string.Empty;
}
