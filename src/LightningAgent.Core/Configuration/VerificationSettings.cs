namespace LightningAgent.Core.Configuration;

public class VerificationSettings
{
    public double DefaultPassThreshold { get; set; } = 0.8;
    public int TimeoutSeconds { get; set; } = 120;
    public int MaxRetries { get; set; } = 2;
}
