using System.Text.Json.Serialization;

namespace LightningAgent.Lightning.LndApiModels;

internal class OpenChannelResponse
{
    [JsonPropertyName("funding_txid_bytes")]
    public string? FundingTxidBytes { get; set; }

    [JsonPropertyName("funding_txid_str")]
    public string? FundingTxidStr { get; set; }

    [JsonPropertyName("output_index")]
    public int OutputIndex { get; set; }
}
