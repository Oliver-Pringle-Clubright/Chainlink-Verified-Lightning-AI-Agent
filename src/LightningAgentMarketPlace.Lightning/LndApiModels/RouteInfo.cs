using System.Text.Json.Serialization;

namespace LightningAgentMarketPlace.Lightning.LndApiModels;

internal class RouteInfo
{
    [JsonPropertyName("total_amt")]
    public string TotalAmt { get; set; } = "0";

    [JsonPropertyName("total_fees")]
    public string TotalFees { get; set; } = "0";

    [JsonPropertyName("hops")]
    public List<HopInfo>? Hops { get; set; }
}
