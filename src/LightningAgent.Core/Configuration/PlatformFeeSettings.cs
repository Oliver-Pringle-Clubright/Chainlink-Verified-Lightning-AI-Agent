namespace LightningAgent.Core.Configuration;

/// <summary>
/// Platform revenue configuration.
/// </summary>
public class PlatformFeeSettings
{
    /// <summary>
    /// Commission rate on milestone payments (0.03 = 3%).
    /// Deducted from the agent payout; client pays the full amount.
    /// </summary>
    public double CommissionRate { get; set; } = 0.03;

    /// <summary>
    /// Flat fee in sats charged when creating a task (anti-spam).
    /// Set to 0 to disable.
    /// </summary>
    public long TaskPostingFeeSats { get; set; } = 100;

    /// <summary>
    /// Flat fee in sats to cover on-chain verification costs (gas + LINK).
    /// Charged per milestone verification. Set to 0 to disable.
    /// </summary>
    public long VerificationFeeSats { get; set; } = 50;
}
