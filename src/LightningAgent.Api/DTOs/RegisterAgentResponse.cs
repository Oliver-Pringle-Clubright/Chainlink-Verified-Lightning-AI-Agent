namespace LightningAgent.Api.DTOs;

public class RegisterAgentResponse
{
    public int AgentId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// The API key is returned only once at registration time.
    /// Store it securely — it cannot be retrieved again.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Warning that the API key will not be shown again.
    /// </summary>
    public string? ApiKeyWarning { get; set; }

    public bool IsEarlyAdopter { get; set; }
    public string? EarlyAdopterMessage { get; set; }
}
