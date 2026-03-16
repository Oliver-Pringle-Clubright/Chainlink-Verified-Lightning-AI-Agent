namespace LightningAgentMarketPlace.Core.Configuration;

public class JwtSettings
{
    public string Secret { get; set; } = "";
    public string Issuer { get; set; } = "LightningAgentMarketPlace";
    public string Audience { get; set; } = "LightningAgentMarketPlace";
    public int ExpiryMinutes { get; set; } = 60;
}
