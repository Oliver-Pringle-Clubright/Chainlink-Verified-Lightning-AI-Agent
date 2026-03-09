using System.Text.Json.Serialization;

namespace LightningAgent.Lightning.LndApiModels;

internal class HopInfo
{
    [JsonPropertyName("chan_id")]
    public string ChanId { get; set; } = string.Empty;

    [JsonPropertyName("pub_key")]
    public string PubKey { get; set; } = string.Empty;
}
