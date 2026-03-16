namespace LightningAgentMarketPlace.Api.DTOs;

public class AgentDetailResponse
{
    public int Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? WalletPubkey { get; set; }
    public string Status { get; set; } = string.Empty;
    public ReputationDto? Reputation { get; set; }
    public List<AgentCapabilityDto>? Capabilities { get; set; }
}
