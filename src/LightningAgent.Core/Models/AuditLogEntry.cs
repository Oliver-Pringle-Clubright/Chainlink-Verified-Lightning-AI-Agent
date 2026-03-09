namespace LightningAgent.Core.Models;

public class AuditLogEntry
{
    public int Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; }
}
