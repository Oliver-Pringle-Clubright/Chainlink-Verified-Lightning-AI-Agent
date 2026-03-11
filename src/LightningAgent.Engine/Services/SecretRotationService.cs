using LightningAgent.Core.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightningAgent.Engine.Services;

public class SecretRotationService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly IOptionsMonitor<ClaudeAiSettings> _claudeSettings;
    private readonly IOptionsMonitor<OpenRouterSettings> _openRouterSettings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SecretRotationService> _logger;

    public SecretRotationService(
        IOptionsMonitor<ClaudeAiSettings> claudeSettings,
        IOptionsMonitor<OpenRouterSettings> openRouterSettings,
        IHttpClientFactory httpClientFactory,
        ILogger<SecretRotationService> logger)
    {
        _claudeSettings = claudeSettings;
        _openRouterSettings = openRouterSettings;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SecretRotationService started - checking API key validity every {Hours} hours",
            CheckInterval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckClaudeApiKeyAsync(stoppingToken);
                await CheckOpenRouterApiKeyAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SecretRotationService encountered an error during key validation check");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("SecretRotationService stopped");
    }

    private async Task CheckClaudeApiKeyAsync(CancellationToken ct)
    {
        var settings = _claudeSettings.CurrentValue;

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            _logger.LogWarning("Claude API key is not configured. AI features will not function");
            return;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models");
            request.Headers.Add("x-api-key", settings.ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

            var response = await client.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Claude API key validation: key is valid");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning(
                    "Claude API key validation FAILED: key is invalid or expired (HTTP 401). " +
                    "Rotate the key via POST /api/secrets/rotate/claude");
            }
            else
            {
                _logger.LogWarning(
                    "Claude API key validation returned unexpected status {StatusCode}. " +
                    "Key may still be valid but the API returned an error",
                    (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not validate Claude API key - Anthropic API may be unreachable");
        }
    }

    private async Task CheckOpenRouterApiKeyAsync(CancellationToken ct)
    {
        var settings = _openRouterSettings.CurrentValue;

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            _logger.LogDebug("OpenRouter API key is not configured, skipping validation");
            return;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var request = new HttpRequestMessage(HttpMethod.Get, $"{settings.BaseUrl}/models");
            request.Headers.Add("Authorization", $"Bearer {settings.ApiKey}");

            var response = await client.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("OpenRouter API key validation: key is valid");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning(
                    "OpenRouter API key validation FAILED: key is invalid or expired (HTTP 401). " +
                    "Rotate the key via POST /api/secrets/rotate/openrouter");
            }
            else
            {
                _logger.LogWarning(
                    "OpenRouter API key validation returned unexpected status {StatusCode}",
                    (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not validate OpenRouter API key - OpenRouter API may be unreachable");
        }
    }
}
