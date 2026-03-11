using System.ComponentModel.DataAnnotations;

namespace LightningAgent.Api.DTOs;

public class CreateTaskRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    [StringLength(10000, MinimumLength = 1)]
    public string Description { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    [StringLength(50)]
    public string TaskType { get; set; } = string.Empty;

    [Range(1, long.MaxValue, ErrorMessage = "MaxPayoutSats must be greater than zero.")]
    public long MaxPayoutSats { get; set; }

    [StringLength(5000)]
    public string? VerificationCriteria { get; set; }

    [Range(0, 100)]
    public int? Priority { get; set; }

    [StringLength(100)]
    public string? ClientId { get; set; }

    public bool UseNaturalLanguage { get; set; }
}
