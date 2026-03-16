namespace LightningAgentMarketPlace.Core.Models.AI;

public class ClaudeResponse
{
    public string Id { get; set; } = string.Empty;
    public List<ClaudeContentBlock> Content { get; set; } = new();
    public string Model { get; set; } = string.Empty;
    public string? StopReason { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}

public class ClaudeContentBlock
{
    public string Type { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}
