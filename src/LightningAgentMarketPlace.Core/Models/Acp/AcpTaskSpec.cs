using System.ComponentModel.DataAnnotations;

namespace LightningAgentMarketPlace.Core.Models.Acp;

public class AcpTaskSpec
{
    [StringLength(100)]
    public string TaskId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    [StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    [StringLength(10000, MinimumLength = 1)]
    public string Description { get; set; } = string.Empty;

    [StringLength(50)]
    public string TaskType { get; set; } = string.Empty;

    [MaxLength(20)]
    public List<string> RequiredSkills { get; set; } = new();

    [Required]
    public AcpBudget Budget { get; set; } = new();

    [StringLength(5000)]
    public string? VerificationRequirements { get; set; }

    public DateTime? Deadline { get; set; }

    public Dictionary<string, string>? Metadata { get; set; }
}

public class AcpBudget
{
    [Range(0, long.MaxValue)]
    public long MaxSats { get; set; }

    [StringLength(10)]
    public string PreferredCurrency { get; set; } = string.Empty;

    [Range(0, double.MaxValue)]
    public double? UsdEquivalent { get; set; }
}
