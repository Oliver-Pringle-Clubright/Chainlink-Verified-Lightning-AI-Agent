namespace LightningAgent.Core.Configuration;

public class ClaudeAiSettings
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "claude-sonnet-4-20250514";
    public int MaxTokens { get; set; } = 4096;
    public double Temperature { get; set; } = 0.3;
}
