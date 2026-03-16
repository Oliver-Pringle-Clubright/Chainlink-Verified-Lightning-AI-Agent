using System.Text.Json.Serialization;

namespace LightningAgentMarketPlace.Lightning.LndApiModels;

internal class AddHodlInvoiceRequest
{
    [JsonPropertyName("memo")]
    public string? Memo { get; set; }

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = "0";

    [JsonPropertyName("expiry")]
    public string Expiry { get; set; } = "3600";
}
