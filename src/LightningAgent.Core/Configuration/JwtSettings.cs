namespace LightningAgent.Core.Configuration;

public class JwtSettings
{
    public string Secret { get; set; } = "";
    public string Issuer { get; set; } = "LightningAgent";
    public string Audience { get; set; } = "LightningAgent";
    public int ExpiryMinutes { get; set; } = 60;
}
