namespace LightningAgentMarketPlace.Core.Models;

public class Artifact
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public int? MilestoneId { get; set; }
    public int? AgentId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string StoragePath { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
