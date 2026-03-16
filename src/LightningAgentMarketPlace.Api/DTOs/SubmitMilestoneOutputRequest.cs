using System.ComponentModel.DataAnnotations;

namespace LightningAgentMarketPlace.Api.DTOs;

public class SubmitMilestoneOutputRequest
{
    [Required(AllowEmptyStrings = false)]
    [MinLength(1)]
    public string OutputData { get; set; } = string.Empty;

    [StringLength(100)]
    public string? ContentType { get; set; }
}
