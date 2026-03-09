namespace LightningAgent.Core.Configuration;

public class LightningSettings
{
    public string LndRestUrl { get; set; } = "https://localhost:8080";
    public string MacaroonPath { get; set; } = "";
    public string TlsCertPath { get; set; } = "";
    public int DefaultInvoiceExpirySec { get; set; } = 3600;
}
