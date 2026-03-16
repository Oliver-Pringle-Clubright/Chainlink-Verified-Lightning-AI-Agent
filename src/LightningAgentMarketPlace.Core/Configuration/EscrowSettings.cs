namespace LightningAgentMarketPlace.Core.Configuration;

public class EscrowSettings
{
    public int DefaultExpirySec { get; set; } = 3600;
    public int MaxRetries { get; set; } = 2;
    public string EncryptionKey { get; set; } = "";
}
