using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LightningAgentMarketPlace.Core.Interfaces.Data;
using Microsoft.Extensions.Logging;

namespace LightningAgentMarketPlace.Engine.Services;

/// <summary>
/// Sends HTTP POST to registered webhook subscriptions when events occur.
/// Payload: { "event": "TaskAssigned", "timestamp": "...", "data": { ... } }
/// Signs payload with HMAC-SHA256 using the subscription's secret (X-Webhook-Signature header).
/// Uses fire-and-forget to avoid blocking the main flow.
/// </summary>
public class WebhookDispatcher
{
    private readonly IWebhookRepository _webhookRepo;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookDispatcher> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public WebhookDispatcher(
        IWebhookRepository webhookRepo,
        IHttpClientFactory httpClientFactory,
        ILogger<WebhookDispatcher> logger)
    {
        _webhookRepo = webhookRepo;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Dispatches an event to all active webhook subscriptions that match the event type.
    /// Runs as fire-and-forget so the caller is not blocked.
    /// </summary>
    public void Dispatch(string eventType, object data)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await DispatchInternalAsync(eventType, data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error dispatching webhook event {EventType}", eventType);
            }
        });
    }

    private async Task DispatchInternalAsync(string eventType, object data)
    {
        var subscriptions = await _webhookRepo.GetActiveByEventAsync(eventType);
        if (subscriptions.Count == 0)
            return;

        var payload = JsonSerializer.Serialize(new
        {
            @event = eventType,
            timestamp = DateTime.UtcNow.ToString("o"),
            data
        }, JsonOptions);

        var tasks = subscriptions.Select(sub => SendWebhookAsync(sub, eventType, payload));
        await Task.WhenAll(tasks);
    }

    private async Task SendWebhookAsync(
        LightningAgentMarketPlace.Core.Models.WebhookSubscription subscription,
        string eventType,
        string payload)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var request = new HttpRequestMessage(HttpMethod.Post, subscription.Url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };

            request.Headers.Add("X-Webhook-Event", eventType);

            // Sign the payload with HMAC-SHA256 if a secret is configured
            if (!string.IsNullOrEmpty(subscription.Secret))
            {
                var keyBytes = Encoding.UTF8.GetBytes(subscription.Secret);
                var payloadBytes = Encoding.UTF8.GetBytes(payload);
                var hash = HMACSHA256.HashData(keyBytes, payloadBytes);
                var signature = Convert.ToHexStringLower(hash);
                request.Headers.Add("X-Webhook-Signature", $"sha256={signature}");
            }

            var response = await client.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Webhook delivered to {Url} for event {EventType}: HTTP {StatusCode}",
                    subscription.Url, eventType, (int)response.StatusCode);
            }
            else
            {
                _logger.LogWarning(
                    "Webhook delivery to {Url} for event {EventType} returned HTTP {StatusCode}",
                    subscription.Url, eventType, (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Webhook delivery to {Url} for event {EventType} failed",
                subscription.Url, eventType);
        }
    }
}
