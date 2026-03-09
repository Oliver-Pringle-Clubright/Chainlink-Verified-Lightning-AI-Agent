namespace LightningAgent.Engine;

using LightningAgent.Core.Interfaces.Data;
using Microsoft.Extensions.Logging;

public class WebhookDeliveryService
{
    private readonly HttpClient _httpClient;
    private readonly IAgentRepository _agentRepo;
    private readonly IAuditLogRepository _auditRepo;
    private readonly ILogger<WebhookDeliveryService> _logger;

    public WebhookDeliveryService(HttpClient httpClient, IAgentRepository agentRepo, IAuditLogRepository auditRepo, ILogger<WebhookDeliveryService> logger)
    {
        _httpClient = httpClient;
        _agentRepo = agentRepo;
        _auditRepo = auditRepo;
        _logger = logger;
    }

    public async Task DeliverAsync(int agentId, string eventType, object payload, CancellationToken ct = default)
    {
        var agent = await _agentRepo.GetByIdAsync(agentId, ct);
        if (agent == null || string.IsNullOrEmpty(agent.WebhookUrl))
            return;

        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                eventType,
                timestamp = DateTime.UtcNow,
                agentId,
                data = payload
            });

            var request = new HttpRequestMessage(HttpMethod.Post, agent.WebhookUrl)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Webhook-Event", eventType);

            var response = await _httpClient.SendAsync(request, ct);

            _logger.LogInformation("Webhook delivered to agent {AgentId}: {EventType} -> {StatusCode}",
                agentId, eventType, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Webhook delivery failed for agent {AgentId}: {EventType}", agentId, eventType);
        }
    }
}
