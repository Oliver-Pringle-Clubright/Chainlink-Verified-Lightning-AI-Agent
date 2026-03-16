using System.ComponentModel.DataAnnotations;

namespace LightningAgentMarketPlace.Api.DTOs;

public class AgentCapabilityDto
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(50)]
    public string SkillType { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    [MaxLength(20)]
    public List<string> TaskTypes { get; set; } = new();

    [Range(1, 100)]
    public int? MaxConcurrency { get; set; }

    [Range(0, long.MaxValue)]
    public long PriceSatsPerUnit { get; set; }
}
