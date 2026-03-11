using System.ComponentModel.DataAnnotations;

namespace LightningAgent.Api.DTOs;

public class RegisterAgentRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(100)]
    public string? ExternalId { get; set; }

    [StringLength(200)]
    public string? WalletPubkey { get; set; }

    [MaxLength(50)]
    public List<AgentCapabilityDto>? Capabilities { get; set; }

    [StringLength(500)]
    [Url]
    public string? WebhookUrl { get; set; }
}
