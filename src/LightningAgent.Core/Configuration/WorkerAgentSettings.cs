namespace LightningAgent.Core.Configuration;

public class WorkerAgentSettings
{
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 30;
    public int MaxConcurrentAgents { get; set; } = 5;
}
