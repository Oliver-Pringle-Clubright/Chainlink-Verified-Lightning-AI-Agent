using System.Text.Json.Serialization;

namespace LightningAgentMarketPlace.Lightning.LndApiModels;

internal class AddHodlInvoiceResponse
{
    [JsonPropertyName("payment_request")]
    public string PaymentRequest { get; set; } = string.Empty;
}
