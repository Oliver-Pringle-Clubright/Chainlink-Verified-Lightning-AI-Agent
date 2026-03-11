using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LightningAgent.Engine;

/// <summary>
/// Validates required configuration at application startup.
/// Returns a summary of configured vs. missing settings.
/// </summary>
public static class StartupValidator
{
    /// <summary>
    /// Validates critical configuration values and logs results.
    /// Throws <see cref="InvalidOperationException"/> for missing required settings
    /// unless <c>ApiSecurity:DevMode</c> is <c>true</c>.
    /// </summary>
    public static StartupValidationResult Validate(IConfiguration configuration, ILogger logger)
    {
        var result = new StartupValidationResult();

        var devMode = string.Equals(
            configuration["ApiSecurity:DevMode"], "true", StringComparison.OrdinalIgnoreCase);

        // ── Required settings (error / throw unless DevMode) ─────────────

        ValidateRequired(configuration, logger, result, devMode,
            "ClaudeAi:ApiKey", "Claude AI API key is required for AI operations.");

        ValidateRequired(configuration, logger, result, devMode,
            "Lightning:LndRestUrl", "Lightning LND REST URL is required for payment operations.");

        // ── Optional but important settings (warn only) ──────────────────

        ValidateOptional(configuration, logger, result,
            "Lightning:MacaroonPath", "LND macaroon path is not configured — LND calls will fail without authentication.");

        ValidateOptional(configuration, logger, result,
            "Lightning:TlsCertPath", "LND TLS certificate path is not configured — LND connections may fail.");

        ValidateOptional(configuration, logger, result,
            "Chainlink:EthereumRpcUrl", "Ethereum RPC URL is not configured — Chainlink features will be disabled.");

        ValidateOptional(configuration, logger, result,
            "Chainlink:PrivateKeyPath", "Ethereum private key path is not configured — on-chain transactions will be disabled.");

        // ── Summary ──────────────────────────────────────────────────────

        if (result.Configured.Count > 0)
        {
            logger.LogInformation(
                "Startup configuration validated: {ConfiguredCount} setting(s) configured [{Configured}]",
                result.Configured.Count, string.Join(", ", result.Configured));
        }

        if (result.Warnings.Count > 0)
        {
            logger.LogWarning(
                "Startup configuration warnings: {WarningCount} setting(s) missing [{Warnings}]",
                result.Warnings.Count, string.Join(", ", result.Warnings));
        }

        if (result.Errors.Count > 0 && devMode)
        {
            logger.LogWarning(
                "DevMode is enabled — skipping startup validation errors for: {Errors}",
                string.Join(", ", result.Errors));
        }

        return result;
    }

    /// <summary>
    /// Queries the Ethereum RPC endpoint for the chain ID and logs the detected network.
    /// Only runs when <c>Chainlink:EthereumRpcUrl</c> is configured.
    /// Never throws — failures are logged as warnings.
    /// </summary>
    public static async Task ValidateChainIdAsync(IConfiguration configuration, ILogger logger)
    {
        var rpcUrl = configuration["Chainlink:EthereumRpcUrl"];
        if (string.IsNullOrWhiteSpace(rpcUrl))
        {
            return;
        }

        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var requestBody = new StringContent(
                """{"jsonrpc":"2.0","method":"eth_chainId","id":1}""",
                Encoding.UTF8,
                "application/json");

            var response = await httpClient.PostAsync(rpcUrl, requestBody);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            if (json.TryGetProperty("result", out var resultProp))
            {
                var hexChainId = resultProp.GetString();
                if (!string.IsNullOrWhiteSpace(hexChainId))
                {
                    var chainId = Convert.ToInt64(hexChainId, 16);
                    var networkName = GetNetworkName(chainId);

                    logger.LogInformation(
                        "Ethereum RPC connected — Chain ID: {ChainId} ({NetworkName})",
                        chainId, networkName);

                    if (chainId == 1)
                    {
                        logger.LogWarning(
                            "Connected to Ethereum MAINNET — all transactions will use real ETH. " +
                            "Set Chainlink:EthereumRpcUrl to a testnet RPC to avoid this.");
                    }
                }
            }
            else if (json.TryGetProperty("error", out var errorProp))
            {
                logger.LogWarning(
                    "Ethereum RPC returned an error during chain ID check: {Error}",
                    errorProp.ToString());
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Unable to verify Ethereum chain ID from {RpcUrl} — the RPC endpoint may be unreachable at startup.",
                rpcUrl);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static void ValidateRequired(
        IConfiguration configuration,
        ILogger logger,
        StartupValidationResult result,
        bool devMode,
        string key,
        string errorMessage)
    {
        var value = configuration[key];

        if (!string.IsNullOrWhiteSpace(value))
        {
            result.Configured.Add(key);
            return;
        }

        result.Errors.Add(key);

        if (devMode)
        {
            logger.LogWarning("Required setting {Key} is not configured (DevMode active, continuing): {Message}",
                key, errorMessage);
        }
        else
        {
            logger.LogError("Required setting {Key} is not configured: {Message}", key, errorMessage);
            throw new InvalidOperationException(
                $"Required configuration '{key}' is missing or empty. {errorMessage} " +
                $"Set the value in appsettings.json, user secrets, or environment variables. " +
                $"Alternatively, set ApiSecurity:DevMode=true to bypass this check during development.");
        }
    }

    private static void ValidateOptional(
        IConfiguration configuration,
        ILogger logger,
        StartupValidationResult result,
        string key,
        string warningMessage)
    {
        var value = configuration[key];

        if (!string.IsNullOrWhiteSpace(value))
        {
            result.Configured.Add(key);
        }
        else
        {
            result.Warnings.Add(key);
            logger.LogWarning("Optional setting {Key} is not configured: {Message}", key, warningMessage);
        }
    }

    private static string GetNetworkName(long chainId) => chainId switch
    {
        1 => "Mainnet",
        5 => "Goerli",
        11155111 => "Sepolia",
        17000 => "Holesky",
        137 => "Polygon",
        80001 => "Mumbai",
        42161 => "Arbitrum One",
        421614 => "Arbitrum Sepolia",
        10 => "Optimism",
        11155420 => "Optimism Sepolia",
        8453 => "Base",
        84532 => "Base Sepolia",
        43114 => "Avalanche",
        43113 => "Avalanche Fuji",
        56 => "BSC",
        _ => $"Unknown (Chain ID {chainId})"
    };
}

/// <summary>
/// Contains the results of startup configuration validation.
/// </summary>
public class StartupValidationResult
{
    /// <summary>Configuration keys that are properly set.</summary>
    public List<string> Configured { get; } = new();

    /// <summary>Configuration keys that triggered warnings (optional but missing).</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>Configuration keys that triggered errors (required but missing).</summary>
    public List<string> Errors { get; } = new();

    /// <summary>True if no errors were found (warnings are acceptable).</summary>
    public bool IsValid => Errors.Count == 0;
}
