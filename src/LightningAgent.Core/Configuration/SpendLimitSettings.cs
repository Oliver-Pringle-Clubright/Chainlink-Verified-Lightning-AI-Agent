namespace LightningAgent.Core.Configuration;

public class SpendLimitSettings
{
    public long DefaultDailyCapSats { get; set; } = 1000000;
    public long DefaultWeeklyCapSats { get; set; } = 5000000;
    public long DefaultPerTaskMaxSats { get; set; } = 500000;
}
