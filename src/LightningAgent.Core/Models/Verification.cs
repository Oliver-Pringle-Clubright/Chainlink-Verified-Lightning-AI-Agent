using LightningAgent.Core.Enums;

namespace LightningAgent.Core.Models;

public class Verification
{
    public int Id { get; set; }
    public int MilestoneId { get; set; }
    public int TaskId { get; set; }
    public VerificationStrategyType StrategyType { get; set; }
    public string? ChainlinkRequestId { get; set; }
    public string? ChainlinkTxHash { get; set; }
    public string? InputHash { get; set; }
    public double? Score { get; set; }
    public bool Passed { get; set; }
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
