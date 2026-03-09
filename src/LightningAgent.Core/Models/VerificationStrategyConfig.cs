using LightningAgent.Core.Enums;

namespace LightningAgent.Core.Models;

public class VerificationStrategyConfig
{
    public int Id { get; set; }
    public VerificationStrategyType StrategyType { get; set; }
    public string ParameterName { get; set; } = string.Empty;
    public string ParameterValue { get; set; } = string.Empty;
    public double LearnedWeight { get; set; } = 1.0;
    public DateTime UpdatedAt { get; set; }
}
