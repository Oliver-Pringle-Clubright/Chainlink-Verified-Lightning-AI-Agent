using System.Text.Json.Serialization;

namespace LightningAgentMarketPlace.Lightning.LndApiModels;

internal class GetInfoResponse
{
    [JsonPropertyName("alias")]
    public string? Alias { get; set; }

    [JsonPropertyName("identity_pubkey")]
    public string IdentityPubkey { get; set; } = string.Empty;

    [JsonPropertyName("num_active_channels")]
    public int NumActiveChannels { get; set; }

    [JsonPropertyName("num_peers")]
    public int NumPeers { get; set; }

    [JsonPropertyName("block_height")]
    public int BlockHeight { get; set; }

    [JsonPropertyName("synced_to_chain")]
    public bool SyncedToChain { get; set; }
}
