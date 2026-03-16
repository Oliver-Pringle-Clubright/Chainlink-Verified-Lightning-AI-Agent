namespace LightningAgentMarketPlace.Core.Models.AI;

public class ClaudeRequest
{
    public string Model { get; set; } = string.Empty;
    public int MaxTokens { get; set; }
    public double? Temperature { get; set; }
    public string? SystemPrompt { get; set; }
    public List<ClaudeMessage> Messages { get; set; } = new();
}

public class ClaudeMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
