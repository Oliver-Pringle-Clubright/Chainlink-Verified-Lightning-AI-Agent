namespace LightningAgentMarketPlace.Core.Models.Acp;

public class AcpServiceDescriptor
{
    public string ServiceId { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> SupportedTaskTypes { get; set; } = new();
    public AcpPriceRange PriceRange { get; set; } = new();
    public string Endpoint { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
}

public class AcpPriceRange
{
    public long MinSats { get; set; }
    public long MaxSats { get; set; }
}
