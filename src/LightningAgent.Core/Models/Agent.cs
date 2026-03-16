using LightningAgent.Core.Enums;

namespace LightningAgent.Core.Models;

public class Agent
{
    public int Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? WalletPubkey { get; set; }
    public AgentStatus Status { get; set; }
    public long DailySpendCapSats { get; set; }
    public long WeeklySpendCapSats { get; set; }
    public string? WebhookUrl { get; set; }
    public string? ApiKeyHash { get; set; }
    public int RateLimitPerMinute { get; set; } = 100;
    public string? EthAddress { get; set; }
    public long? PreferredChainId { get; set; }
    public string? PreferredPaymentMethod { get; set; }
    public bool IsEarlyAdopter { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
