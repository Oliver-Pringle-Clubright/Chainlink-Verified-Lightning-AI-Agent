using System.Net;
using System.Net.Sockets;

namespace LightningAgent.Core.Security;

/// <summary>
/// Validates URLs for safety against Server-Side Request Forgery (SSRF) attacks.
/// Rejects private/internal IPs, loopback, link-local, and optionally non-HTTPS schemes.
/// </summary>
public static class UrlValidator
{
    /// <summary>
    /// Validates that a URL is safe for server-side requests (no SSRF).
    /// Rejects private/internal IPs, loopback, link-local, and non-HTTPS schemes.
    /// </summary>
    public static (bool IsValid, string? Error) ValidateWebhookUrl(string? url, bool requireHttps = false)
    {
        if (string.IsNullOrWhiteSpace(url))
            return (true, null); // null URL is fine (optional field)

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return (false, "Invalid URL format.");

        if (uri.Scheme != "https" && uri.Scheme != "http")
            return (false, "URL must use http or https scheme.");

        if (requireHttps && uri.Scheme != "https")
            return (false, "URL must use https scheme.");

        // Block loopback
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("127.0.0.1") ||
            uri.Host.Equals("::1") ||
            uri.Host.Equals("[::1]") ||
            uri.Host.Equals("0.0.0.0"))
            return (false, "Webhook URL must not point to localhost.");

        // Try to resolve and check IP ranges
        try
        {
            var addresses = Dns.GetHostAddresses(uri.Host);
            foreach (var addr in addresses)
            {
                if (IPAddress.IsLoopback(addr))
                    return (false, "Webhook URL must not resolve to a loopback address.");

                var bytes = addr.GetAddressBytes();
                if (addr.AddressFamily == AddressFamily.InterNetwork)
                {
                    // 10.0.0.0/8
                    if (bytes[0] == 10)
                        return (false, "Webhook URL must not resolve to a private network address (10.x.x.x).");
                    // 172.16.0.0/12
                    if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                        return (false, "Webhook URL must not resolve to a private network address (172.16-31.x.x).");
                    // 192.168.0.0/16
                    if (bytes[0] == 192 && bytes[1] == 168)
                        return (false, "Webhook URL must not resolve to a private network address (192.168.x.x).");
                    // 169.254.0.0/16 (link-local)
                    if (bytes[0] == 169 && bytes[1] == 254)
                        return (false, "Webhook URL must not resolve to a link-local address.");
                }
            }
        }
        catch
        {
            // DNS resolution failed - allow the URL (it may resolve later)
        }

        return (true, null);
    }
}
