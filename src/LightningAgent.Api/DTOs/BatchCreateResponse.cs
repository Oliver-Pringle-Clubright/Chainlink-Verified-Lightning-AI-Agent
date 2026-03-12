namespace LightningAgent.Api.DTOs;

public class BatchCreateResponse
{
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public List<BatchCreateResult> Results { get; set; } = new();
}

public class BatchCreateResult
{
    public bool Success { get; set; }
    public int? TaskId { get; set; }
    public string? ExternalId { get; set; }
    public string? Error { get; set; }
}
