using System.ComponentModel.DataAnnotations;

namespace LightningAgentMarketPlace.Api.DTOs;

public class UpdateRateRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(50)]
    public string SkillType { get; set; } = string.Empty;

    [Range(0, long.MaxValue)]
    public long PricePerUnit { get; set; }
}
