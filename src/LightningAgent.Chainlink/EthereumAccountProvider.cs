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

        var resolvedPath = ResolveKeyPath(privateKeyPath);
        ValidateFilePath(resolvedPath);
        var privateKey = File.ReadAllText(resolvedPath).Trim();
        return new Account(privateKey);
    }

    /// <summary>
    /// Resolves relative paths by searching: working dir, then up to 3 parent directories.
    /// This handles dotnet run from src/LightningAgent.Api/ when secrets/ is at project root.
    /// </summary>
    private static string ResolveKeyPath(string path)
    {
        if (Path.IsPathRooted(path) && File.Exists(path))
            return path;

        if (File.Exists(path))
            return Path.GetFullPath(path);

        // Walk up directories looking for the file
        var dir = Directory.GetCurrentDirectory();
        for (int i = 0; i < 4; i++)
        {
            var candidate = Path.Combine(dir, path);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);

            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }

        // Return original path — will fail with FileNotFoundException (better than silent)
        return path;
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
