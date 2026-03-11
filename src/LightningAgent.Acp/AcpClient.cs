using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LightningAgent.Core.Configuration;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models.Acp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightningAgent.Acp;

/// <summary>
/// HTTP client for communicating with an ACP (Agent Communication Protocol) service.
/// </summary>
public class AcpClient : IAcpClient
{
    private readonly HttpClient _httpClient;
    private readonly AcpSettings _settings;
    private readonly ILogger<AcpClient> _logger;

    public AcpClient(
        HttpClient httpClient,
        IOptions<AcpSettings> settings,
        ILogger<AcpClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<List<AcpServiceDescriptor>> DiscoverServicesAsync(
        string? taskType = null,
        CancellationToken ct = default)
    {
        try
        {
            var url = string.IsNullOrEmpty(taskType)
                ? "/api/acp/services"
                : $"/api/acp/services?taskType={Uri.EscapeDataString(taskType)}";

            _logger.LogInformation("Discovering ACP services at {Url}", url);

            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var services = await response.Content.ReadFromJsonAsync<List<AcpServiceDescriptor>>(
                AcpJsonOptions.Default, ct);

            _logger.LogInformation("Discovered {Count} ACP services", services?.Count ?? 0);
            return services ?? new List<AcpServiceDescriptor>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to discover ACP services (no ACP server configured or unreachable). Returning empty list");
            return new List<AcpServiceDescriptor>();
        }
    }

    public async Task<string> PostTaskAsync(AcpTaskSpec task, CancellationToken ct = default)
    {
        const int maxRetries = 3;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                _logger.LogInformation(
                    "Posting ACP task: {Title} (attempt {Attempt}/{Max})",
                    task.Title, attempt, maxRetries);

                var json = AcpMessageSerializer.Serialize(task);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Add HMAC-SHA256 signature if API key is configured
                if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
                {
                    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                    var signaturePayload = $"{timestamp}.{json}";
                    using var hmac = new HMACSHA256(
                        Encoding.UTF8.GetBytes(_settings.ApiKey));
                    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signaturePayload));
                    var signature = Convert.ToHexString(hash).ToLowerInvariant();

                    content.Headers.Add("X-ACP-Timestamp", timestamp);
                    content.Headers.Add("X-ACP-Signature", signature);
                }

                var response = await _httpClient.PostAsync("/api/acp/tasks", content, ct);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync(ct);
                var result = JsonSerializer.Deserialize<JsonElement>(responseBody);

                var taskId = result.TryGetProperty("taskId", out var idProp)
                    ? idProp.GetString() ?? task.TaskId
                    : task.TaskId;

                _logger.LogInformation("ACP task posted successfully with ID {TaskId}", taskId);
                return taskId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "ACP task post failed (attempt {Attempt}/{Max}): {Error}",
                    attempt, maxRetries, ex.Message);

                if (attempt < maxRetries)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(4, attempt - 1)); // 1s, 4s, 16s
                    _logger.LogInformation("Retrying ACP task post in {Delay}s", delay.TotalSeconds);
                    await Task.Delay(delay, ct);
                }
            }
        }

        // All retries exhausted — fall back to local ID
        var localId = Guid.NewGuid().ToString("N");
        _logger.LogError(
            "All {MaxRetries} ACP task post attempts failed. Generated local task ID {LocalTaskId}. " +
            "The task may not have reached the ACP server.",
            maxRetries, localId);
        return localId;
    }

    public async Task<List<AcpAgentOffer>> ReceiveOffersAsync(
        string taskId,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Receiving offers for ACP task {TaskId}", taskId);

            var response = await _httpClient.GetAsync($"/api/acp/tasks/{taskId}/offers", ct);
            response.EnsureSuccessStatusCode();

            var offers = await response.Content.ReadFromJsonAsync<List<AcpAgentOffer>>(
                AcpJsonOptions.Default, ct);

            _logger.LogInformation("Received {Count} offers for task {TaskId}", offers?.Count ?? 0, taskId);
            return offers ?? new List<AcpAgentOffer>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to receive offers for ACP task {TaskId}", taskId);
            return new List<AcpAgentOffer>();
        }
    }

    public async Task<bool> AcceptOfferAsync(string offerId, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Accepting ACP offer {OfferId}", offerId);

            var response = await _httpClient.PostAsync(
                $"/api/acp/offers/{offerId}/accept",
                new StringContent("{}", Encoding.UTF8, "application/json"),
                ct);

            response.EnsureSuccessStatusCode();

            _logger.LogInformation("ACP offer {OfferId} accepted", offerId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to accept ACP offer {OfferId}", offerId);
            return false;
        }
    }

    public async Task<bool> NotifyCompletionAsync(
        string taskId,
        string result,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Notifying completion of ACP task {TaskId}", taskId);

            var body = AcpMessageSerializer.Serialize(new { result });
            var content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"/api/acp/tasks/{taskId}/complete",
                content,
                ct);

            response.EnsureSuccessStatusCode();

            _logger.LogInformation("ACP task {TaskId} completion notified", taskId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to notify completion for ACP task {TaskId}", taskId);
            return false;
        }
    }

    /// <summary>
    /// Shared JSON serializer options for ACP HTTP responses.
    /// </summary>
    private static class AcpJsonOptions
    {
        public static readonly JsonSerializerOptions Default = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }
}
