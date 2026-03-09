using System.Text.Json.Serialization;

namespace LightningAgent.Lightning.LndApiModels;

internal class SendPaymentResponse
{
    [JsonPropertyName("payment_hash")]
    public string PaymentHash { get; set; } = string.Empty;

    [JsonPropertyName("payment_preimage")]
    public string PaymentPreimage { get; set; } = string.Empty;

    [JsonPropertyName("value_sat")]
    public string ValueSat { get; set; } = "0";

    [JsonPropertyName("fee_sat")]
    public string FeeSat { get; set; } = "0";

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("failure_reason")]
    public string? FailureReason { get; set; }

    [JsonPropertyName("htlcs")]
    public List<HtlcAttempt>? Htlcs { get; set; }
}
