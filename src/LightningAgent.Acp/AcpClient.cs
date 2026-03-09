using System.Net.Http.Json;
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
        try
        {
            _logger.LogInformation("Posting ACP task: {Title}", task.Title);

            var json = AcpMessageSerializer.Serialize(task);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

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
            var localId = Guid.NewGuid().ToString("N");
            _logger.LogWarning(
                ex,
                "Failed to post ACP task. Generated local task ID {LocalTaskId}",
                localId);
            return localId;
        }
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
