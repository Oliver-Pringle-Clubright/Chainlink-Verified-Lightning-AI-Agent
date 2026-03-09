namespace LightningAgent.Api.DTOs;

public class RegisterAgentRequest
{
    public string Name { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public string? WalletPubkey { get; set; }
    public List<AgentCapabilityDto>? Capabilities { get; set; }
    public string? WebhookUrl { get; set; }
}
