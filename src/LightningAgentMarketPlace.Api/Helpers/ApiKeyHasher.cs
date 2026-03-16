namespace LightningAgentMarketPlace.Api.Helpers;

public static class ApiKeyHasher
{
    public static string Hash(string apiKey) => LightningAgentMarketPlace.Core.Security.ApiKeyHasher.Hash(apiKey);
    public static bool Verify(string apiKey, string storedHash) => LightningAgentMarketPlace.Core.Security.ApiKeyHasher.Verify(apiKey, storedHash);
}
