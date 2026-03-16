using System.Text.Json.Serialization;

namespace LightningAgentMarketPlace.Lightning.LndApiModels;

internal class ChannelBalanceResponse
{
    [JsonPropertyName("local_balance")]
    public BalanceAmount? LocalBalance { get; set; }

    [JsonPropertyName("remote_balance")]
    public BalanceAmount? RemoteBalance { get; set; }

    [JsonPropertyName("unsettled_local_balance")]
    public BalanceAmount? UnsettledLocalBalance { get; set; }

    [JsonPropertyName("unsettled_remote_balance")]
    public BalanceAmount? UnsettledRemoteBalance { get; set; }

    [JsonPropertyName("pending_open_local_balance")]
    public BalanceAmount? PendingOpenLocalBalance { get; set; }

    [JsonPropertyName("pending_open_remote_balance")]
    public BalanceAmount? PendingOpenRemoteBalance { get; set; }
}

internal class BalanceAmount
{
    [JsonPropertyName("sat")]
    public string Sat { get; set; } = "0";

    [JsonPropertyName("msat")]
    public string Msat { get; set; } = "0";
}
