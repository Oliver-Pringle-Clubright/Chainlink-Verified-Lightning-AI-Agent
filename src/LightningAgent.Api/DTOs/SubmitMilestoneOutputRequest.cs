namespace LightningAgent.Api.DTOs;

public class SubmitMilestoneOutputRequest
{
    public string OutputData { get; set; } = string.Empty;
    public string? ContentType { get; set; }
}
