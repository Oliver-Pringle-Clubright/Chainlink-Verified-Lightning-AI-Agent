namespace LightningAgent.Api.DTOs;

public class RegisterAgentResponse
{
    public int AgentId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
