using System.Text.Json.Serialization;

namespace LightningAgentMarketPlace.Lightning.LndApiModels;

internal class InvoiceLookupResponse
{
    [JsonPropertyName("memo")]
    public string? Memo { get; set; }

    [JsonPropertyName("value")]
    public string Value { get; set; } = "0";

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("payment_request")]
    public string? PaymentRequest { get; set; }

    [JsonPropertyName("r_hash")]
    public string RHash { get; set; } = string.Empty;

    [JsonPropertyName("settle_date")]
    public string? SettleDate { get; set; }

    [JsonPropertyName("creation_date")]
    public string? CreationDate { get; set; }

    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }
}
