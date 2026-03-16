using System.ComponentModel.DataAnnotations;

namespace LightningAgentMarketPlace.Api.DTOs;

public class PriceEstimateRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(50)]
    public string TaskType { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    [StringLength(5000)]
    public string Description { get; set; } = string.Empty;

    [StringLength(20)]
    public string? EstimatedComplexity { get; set; }
}
