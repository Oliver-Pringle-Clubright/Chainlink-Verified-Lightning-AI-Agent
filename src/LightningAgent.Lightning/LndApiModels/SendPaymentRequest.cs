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

    [JsonPropertyName("amt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Amt { get; set; }

    [JsonPropertyName("allow_self_payment")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AllowSelfPayment { get; set; }

    [JsonPropertyName("max_parts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxParts { get; set; }
}
