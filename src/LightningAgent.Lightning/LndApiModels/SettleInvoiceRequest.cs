using System.Text.Json.Serialization;

namespace LightningAgent.Lightning.LndApiModels;

internal class SettleInvoiceRequest
{
    [JsonPropertyName("preimage")]
    public string Preimage { get; set; } = string.Empty;
}
