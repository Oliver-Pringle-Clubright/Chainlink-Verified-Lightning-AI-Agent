using LightningAgent.Core.Enums;

namespace LightningAgent.Core.Models;

public class VerificationStrategyParam
{
    public int Id { get; set; }
    public VerificationStrategyType StrategyType { get; set; }
    public string ParameterName { get; set; } = string.Empty;
    public string ParameterValue { get; set; } = string.Empty;
    public double LearnedWeight { get; set; }
    public DateTime UpdatedAt { get; set; }
}
