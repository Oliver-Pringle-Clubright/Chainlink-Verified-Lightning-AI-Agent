using System.Text;
using LightningAgentMarketPlace.Core.Configuration;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightningAgentMarketPlace.Chainlink.Services;

public class AutomationService
{
    private readonly IChainlinkAutomationClient _automationClient;
    private readonly ChainlinkSettings _settings;
    private readonly ILogger<AutomationService> _logger;

    /// <summary>
    /// Default gas limit for upkeep check + perform operations.
    /// </summary>
    private const int DefaultUpkeepGasLimit = 500_000;

    public AutomationService(
        IChainlinkAutomationClient automationClient,
        IOptions<ChainlinkSettings> settings,
        ILogger<AutomationService> logger)
    {
        _automationClient = automationClient;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Registers a Chainlink Automation upkeep that monitors escrow contracts for expiry.
    /// The actual check/perform logic runs on-chain in the target contract.
    /// </summary>
    /// <param name="escrowContractAddress">The address of the escrow contract to monitor.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The upkeep registration transaction hash, or null if automation is not configured.</returns>
    public async Task<string?> RegisterEscrowExpiryUpkeepAsync(
        string escrowContractAddress,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_settings.AutomationRegistryAddress))
        {
            _logger.LogDebug(
                "Chainlink Automation registry not configured, skipping escrow expiry upkeep registration");
            return null;
        }

        _logger.LogInformation(
            "Registering escrow expiry upkeep for contract {EscrowContract}",
            escrowContractAddress);

        var checkData = Encoding.UTF8.GetBytes("checkEscrowExpiry");

        var txHash = await _automationClient.RegisterUpkeepAsync(
            escrowContractAddress,
            checkData,
            DefaultUpkeepGasLimit,
            ct);

        _logger.LogInformation(
            "Escrow expiry upkeep registered, txHash={TxHash}, target={Target}",
            txHash, escrowContractAddress);

        return txHash;
    }

    /// <summary>
    /// Registers a Chainlink Automation upkeep that monitors task contracts for timeout.
    /// The actual check/perform logic runs on-chain in the target contract.
    /// </summary>
    /// <param name="taskContractAddress">The address of the task contract to monitor.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The upkeep registration transaction hash, or null if automation is not configured.</returns>
    public async Task<string?> RegisterTaskTimeoutUpkeepAsync(
        string taskContractAddress,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_settings.AutomationRegistryAddress))
        {
            _logger.LogDebug(
                "Chainlink Automation registry not configured, skipping task timeout upkeep registration");
            return null;
        }

        _logger.LogInformation(
            "Registering task timeout upkeep for contract {TaskContract}",
            taskContractAddress);

        var checkData = Encoding.UTF8.GetBytes("checkTaskTimeout");

        var txHash = await _automationClient.RegisterUpkeepAsync(
            taskContractAddress,
            checkData,
            DefaultUpkeepGasLimit,
            ct);

        _logger.LogInformation(
            "Task timeout upkeep registered, txHash={TxHash}, target={Target}",
            txHash, taskContractAddress);

        return txHash;
    }
}
