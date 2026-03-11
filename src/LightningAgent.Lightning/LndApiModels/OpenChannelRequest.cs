using System.Text.Json.Serialization;

namespace LightningAgent.Lightning.LndApiModels;

internal class OpenChannelRequest
{
    [JsonPropertyName("node_pubkey_string")]
    public string NodePubkeyString { get; set; } = string.Empty;

    [JsonPropertyName("local_funding_amount")]
    public string LocalFundingAmount { get; set; } = "0";

    [JsonPropertyName("push_sat")]
    public string PushSat { get; set; } = "0";

    [JsonPropertyName("private")]
    public bool Private { get; set; }
}
