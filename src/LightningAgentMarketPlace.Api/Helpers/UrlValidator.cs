using System.Net;

namespace LightningAgentMarketPlace.Api.Helpers;

/// <summary>
/// Validates URLs for safety against Server-Side Request Forgery (SSRF) attacks.
/// Thin wrapper over <see cref="LightningAgentMarketPlace.Core.Security.UrlValidator"/> for use in the API layer.
/// </summary>
public static class UrlValidator
{
    /// <inheritdoc cref="Core.Security.UrlValidator.ValidateWebhookUrl"/>
    public static (bool IsValid, string? Error) ValidateWebhookUrl(string? url, bool requireHttps = false)
        => Core.Security.UrlValidator.ValidateWebhookUrl(url, requireHttps);
}
