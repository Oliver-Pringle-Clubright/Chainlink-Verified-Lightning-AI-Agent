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
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
