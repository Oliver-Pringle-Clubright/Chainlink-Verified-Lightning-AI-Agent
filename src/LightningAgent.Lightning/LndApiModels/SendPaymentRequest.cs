using System.Text.Json.Serialization;

namespace LightningAgent.Lightning.LndApiModels;

internal class SendPaymentRequest
{
    [JsonPropertyName("payment_request")]
    public string PaymentRequest { get; set; } = string.Empty;

    [JsonPropertyName("timeout_seconds")]
    public int TimeoutSeconds { get; set; } = 60;

    [JsonPropertyName("fee_limit_sat")]
    public string FeeLimitSat { get; set; } = "100";
}
