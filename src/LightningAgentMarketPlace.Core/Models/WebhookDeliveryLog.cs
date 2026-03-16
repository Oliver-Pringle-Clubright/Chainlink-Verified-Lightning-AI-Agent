namespace LightningAgentMarketPlace.Core.Models;

public class WebhookDeliveryLog
{
    public int Id { get; set; }
    public string WebhookUrl { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime LastAttemptAt { get; set; }
    public WebhookDeliveryStatus Status { get; set; } = WebhookDeliveryStatus.Pending;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum WebhookDeliveryStatus
{
    Pending,
    Delivered,
    Failed
}
