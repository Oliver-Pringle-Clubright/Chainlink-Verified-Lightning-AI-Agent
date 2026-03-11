using LightningAgent.Core.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace LightningAgent.Api.HealthChecks;

public class ClaudeApiHealthCheck : IHealthCheck
{
    private readonly ClaudeAiSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ClaudeApiHealthCheck> _logger;

    public ClaudeApiHealthCheck(
        IOptions<ClaudeAiSettings> settings,
        IHttpClientFactory httpClientFactory,
        ILogger<ClaudeApiHealthCheck> logger)
    {
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // 1. Verify API key is configured
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            return HealthCheckResult.Unhealthy(
                "Claude API key is not configured. Set the ClaudeAi:ApiKey configuration value.");
        }

        // 2. Optionally verify connectivity to the Anthropic API
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var request = new HttpRequestMessage(HttpMethod.Head, "https://api.anthropic.com");
            var response = await client.SendAsync(request, cancellationToken);

            // We don't need a 200; any response (even 401/405) proves connectivity
            _logger.LogDebug(
                "Claude API connectivity check returned {StatusCode}",
                (int)response.StatusCode);

            return HealthCheckResult.Healthy(
                $"Claude API key is configured (model: {_settings.Model}). " +
                $"Anthropic API reachable (HTTP {(int)response.StatusCode}).");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Claude API connectivity check failed");

            return HealthCheckResult.Degraded(
                $"Claude API key is configured but connectivity check failed: {ex.Message}",
                ex);
        }
    }
}
