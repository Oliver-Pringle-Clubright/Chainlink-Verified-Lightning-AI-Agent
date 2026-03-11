namespace LightningAgent.Api.Helpers;

public static class ApiKeyHasher
{
    public static string Hash(string apiKey) => LightningAgent.Core.Security.ApiKeyHasher.Hash(apiKey);
    public static bool Verify(string apiKey, string storedHash) => LightningAgent.Core.Security.ApiKeyHasher.Verify(apiKey, storedHash);
}
