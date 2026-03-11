namespace LightningAgent.Engine;

using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using Microsoft.Extensions.Logging;

public class WebhookDeliveryService
{
    private const int MaxRetries = 3;
    private static readonly int[] RetryDelaysMs = [1_000, 4_000, 16_000];

    private readonly HttpClient _httpClient;
    private readonly IAgentRepository _agentRepo;
    private readonly IAuditLogRepository _auditRepo;
    private readonly IWebhookLogRepository _webhookLogRepo;
    private readonly ILogger<WebhookDeliveryService> _logger;

    public WebhookDeliveryService(
        HttpClient httpClient,
        IAgentRepository agentRepo,
        IAuditLogRepository auditRepo,
        IWebhookLogRepository webhookLogRepo,
        ILogger<WebhookDeliveryService> logger)
    {
        _httpClient = httpClient;
        _agentRepo = agentRepo;
        _auditRepo = auditRepo;
        _webhookLogRepo = webhookLogRepo;
        _logger = logger;
    }

    public async Task DeliverAsync(int agentId, string eventType, object payload, CancellationToken ct = default)
    {
        var agent = await _agentRepo.GetByIdAsync(agentId, ct);
        if (agent == null || string.IsNullOrEmpty(agent.WebhookUrl))
            return;

        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            eventType,
            timestamp = DateTime.UtcNow,
            agentId,
            data = payload
        });

        var logEntry = new WebhookDeliveryLog
        {
            WebhookUrl = agent.WebhookUrl,
            EventType = eventType,
            Payload = json,
            Attempts = 0,
            LastAttemptAt = DateTime.UtcNow,
            Status = WebhookDeliveryStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        logEntry.Id = await _webhookLogRepo.LogAsync(logEntry, ct);

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                var delayMs = RetryDelaysMs[attempt - 1];
                _logger.LogInformation(
                    "Webhook retry {Attempt}/{MaxRetries} for agent {AgentId} event {EventType} after {DelayMs}ms",
                    attempt, MaxRetries, agentId, eventType, delayMs);

                try
                {
                    await Task.Delay(delayMs, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
            }

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, agent.WebhookUrl)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                };
                request.Headers.Add("X-Webhook-Event", eventType);

                var response = await _httpClient.SendAsync(request, ct);

                logEntry.Attempts = attempt + 1;
                logEntry.LastAttemptAt = DateTime.UtcNow;

                if (response.IsSuccessStatusCode)
                {
                    logEntry.Status = WebhookDeliveryStatus.Delivered;
                    logEntry.ErrorMessage = null;
                    await _webhookLogRepo.UpdateAsync(logEntry, ct);

                    _logger.LogInformation(
                        "Webhook delivered to agent {AgentId}: {EventType} -> {StatusCode} (attempt {Attempt})",
                        agentId, eventType, response.StatusCode, attempt + 1);
                    return;
                }

                // Non-success status code — record error and retry
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                logEntry.ErrorMessage = $"HTTP {(int)response.StatusCode}: {errorBody}";
                await _webhookLogRepo.UpdateAsync(logEntry, ct);

                _logger.LogWarning(
                    "Webhook delivery attempt {Attempt} failed for agent {AgentId}: {EventType} -> HTTP {StatusCode}",
                    attempt + 1, agentId, eventType, (int)response.StatusCode);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logEntry.Attempts = attempt + 1;
                logEntry.LastAttemptAt = DateTime.UtcNow;
                logEntry.ErrorMessage = ex.Message;
                await _webhookLogRepo.UpdateAsync(logEntry, ct);

                _logger.LogWarning(ex,
                    "Webhook delivery attempt {Attempt} threw for agent {AgentId}: {EventType}",
                    attempt + 1, agentId, eventType);
            }
        }

        // All retries exhausted — mark as permanently failed (dead letter)
        logEntry.Status = WebhookDeliveryStatus.Failed;
        await _webhookLogRepo.UpdateAsync(logEntry, ct);

        _logger.LogError(
            "Webhook delivery permanently failed for agent {AgentId}: {EventType} after {MaxAttempts} attempts. Entry moved to dead letter.",
            agentId, eventType, MaxRetries + 1);
    }
}
