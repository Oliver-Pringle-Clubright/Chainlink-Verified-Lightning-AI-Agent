namespace LightningAgent.Api.DTOs;

public class PriceEstimateRequest
{
    public string TaskType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? EstimatedComplexity { get; set; }
}
