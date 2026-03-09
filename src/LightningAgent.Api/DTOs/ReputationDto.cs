namespace LightningAgent.Api.DTOs;

public class ReputationDto
{
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int VerificationPasses { get; set; }
    public int VerificationFails { get; set; }
    public double ReputationScore { get; set; }
}
