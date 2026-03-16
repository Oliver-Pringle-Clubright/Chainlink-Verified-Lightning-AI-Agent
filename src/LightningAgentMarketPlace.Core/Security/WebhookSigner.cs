using System.Security.Cryptography;
using System.Text;

namespace LightningAgentMarketPlace.Core.Security;

public static class WebhookSigner
{
    /// <summary>
    /// Compute HMAC-SHA256 signature for webhook payload.
    /// Returns hex-encoded signature string.
    /// </summary>
    public static string Sign(string payload, string secret)
    {
        if (string.IsNullOrEmpty(secret))
            return string.Empty;

        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(keyBytes, payloadBytes);
        return $"sha256={Convert.ToHexStringLower(hash)}";
    }
}
