using System.Text.Json.Serialization;

namespace LightningAgent.Lightning.LndApiModels;

internal class HtlcAttempt
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("route")]
    public RouteInfo? Route { get; set; }
}
