namespace LightningAgent.Api.DTOs;

public class CreateTaskRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public long MaxPayoutSats { get; set; }
    public string? VerificationCriteria { get; set; }
    public int? Priority { get; set; }
    public string? ClientId { get; set; }
    public bool UseNaturalLanguage { get; set; }
}
