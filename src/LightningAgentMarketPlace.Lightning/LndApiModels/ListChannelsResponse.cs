using System.Text.Json.Serialization;

namespace LightningAgentMarketPlace.Lightning.LndApiModels;

internal class ListChannelsResponse
{
    [JsonPropertyName("channels")]
    public List<ChannelInfo>? Channels { get; set; }
}

internal class ChannelInfo
{
    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("remote_pubkey")]
    public string RemotePubkey { get; set; } = string.Empty;

    [JsonPropertyName("channel_point")]
    public string ChannelPoint { get; set; } = string.Empty;

    [JsonPropertyName("capacity")]
    public string Capacity { get; set; } = "0";

    [JsonPropertyName("local_balance")]
    public string LocalBalance { get; set; } = "0";

    [JsonPropertyName("remote_balance")]
    public string RemoteBalance { get; set; } = "0";

    [JsonPropertyName("total_satoshis_sent")]
    public string TotalSatoshisSent { get; set; } = "0";

    [JsonPropertyName("total_satoshis_received")]
    public string TotalSatoshisReceived { get; set; } = "0";

    [JsonPropertyName("num_updates")]
    public string NumUpdates { get; set; } = "0";

    [JsonPropertyName("chan_id")]
    public string ChanId { get; set; } = "0";
}
