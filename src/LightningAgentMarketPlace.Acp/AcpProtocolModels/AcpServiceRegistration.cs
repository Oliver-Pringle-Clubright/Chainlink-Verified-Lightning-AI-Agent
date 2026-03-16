namespace LightningAgentMarketPlace.Acp.AcpProtocolModels;

/// <summary>
/// Wire format for registering this agent network as an ACP service.
/// </summary>
public class AcpServiceRegistration
{
    public string ServiceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> SupportedTaskTypes { get; set; } = new();
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Pricing model: "per_task", "per_milestone", or "streaming".
    /// </summary>
    public string PricingModel { get; set; } = "per_task";
}
