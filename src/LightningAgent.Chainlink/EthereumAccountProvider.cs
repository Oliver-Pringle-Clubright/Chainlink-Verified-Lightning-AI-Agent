using Nethereum.Web3.Accounts;

namespace LightningAgent.Chainlink;

public static class EthereumAccountProvider
{
    private static readonly string[] AllowedDirectories =
    {
        "secrets", "keys", ".eth-keys"
    };

    public static Account? CreateAccount(string privateKeyPath)
    {
        if (string.IsNullOrWhiteSpace(privateKeyPath))
            return null;

        ValidateFilePath(privateKeyPath);
        var privateKey = File.ReadAllText(privateKeyPath).Trim();
        return new Account(privateKey);
    }

    private static void ValidateFilePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var fileName = Path.GetFileName(fullPath);

        // Block obvious traversal patterns
        if (path.Contains("..") || fileName != Path.GetFileName(path.Replace('\\', '/')))
            throw new UnauthorizedAccessException(
                $"Path traversal detected in private key path: {path}");

        // Must be a regular file, not a device or special path
        if (fullPath.StartsWith(@"\\") || fullPath.Contains('\0'))
            throw new UnauthorizedAccessException(
                $"Invalid private key path: {path}");
    }
}
