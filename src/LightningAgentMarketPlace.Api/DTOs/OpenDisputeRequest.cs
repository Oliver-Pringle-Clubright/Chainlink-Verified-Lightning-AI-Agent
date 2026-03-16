using System.ComponentModel.DataAnnotations;

namespace LightningAgentMarketPlace.Api.DTOs;

public class OpenDisputeRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "TaskId must be a positive integer.")]
    public int TaskId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "MilestoneId must be a positive integer.")]
    public int? MilestoneId { get; set; }

    [Required(AllowEmptyStrings = false)]
    [StringLength(50, MinimumLength = 1)]
    public string InitiatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    [StringLength(100, MinimumLength = 1)]
    public string InitiatorId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    [StringLength(2000, MinimumLength = 1)]
    public string Reason { get; set; } = string.Empty;

    [Range(0, long.MaxValue)]
    public long AmountDisputedSats { get; set; }
}
