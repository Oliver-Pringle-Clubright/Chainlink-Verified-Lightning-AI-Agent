using System.Security.Cryptography;
using System.Text;

namespace LightningAgentMarketPlace.Core.Security;

public static class ApiKeyHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public static string Hash(string apiKey)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(apiKey, salt, Iterations, Algorithm, HashSize);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string apiKey, string storedHash)
    {
        if (string.IsNullOrEmpty(storedHash))
            return false;

        // Legacy SHA256 format (no colon) — support existing keys
        if (!storedHash.Contains(':'))
        {
            var legacyHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(legacyHash),
                Encoding.UTF8.GetBytes(storedHash));
        }

        var parts = storedHash.Split(':', 2);
        if (parts.Length != 2) return false;

        try
        {
            var salt = Convert.FromBase64String(parts[0]);
            var expectedHash = Convert.FromBase64String(parts[1]);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(apiKey, salt, Iterations, Algorithm, HashSize);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch
        {
            return false;
        }
    }
}
