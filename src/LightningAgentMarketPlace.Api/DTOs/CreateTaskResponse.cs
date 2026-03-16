namespace LightningAgentMarketPlace.Api.DTOs;

public class CreateTaskResponse
{
    public int TaskId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
