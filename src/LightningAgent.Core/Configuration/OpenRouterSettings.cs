namespace LightningAgent.Core.Configuration;

public class OpenRouterSettings
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public string DefaultModel { get; set; } = "anthropic/claude-sonnet-4-20250514";
    public Dictionary<string, string> TaskTypeModels { get; set; } = new();
}
