using LightningAgent.Core.Models;

namespace LightningAgent.Core.Interfaces.Services;

/// <summary>
/// Provides in-memory cached access to frequently-read data,
/// reducing database round-trips for hot-path queries.
/// </summary>
public interface ICachedDataService
{
    /// <summary>
    /// Returns an agent by ID, cached for 2 minutes.
    /// </summary>
    Task<Agent?> GetAgentAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Returns the reputation record for the given agent, cached for 1 minute.
    /// </summary>
    Task<AgentReputation?> GetAgentReputationAsync(int agentId, CancellationToken ct = default);

    /// <summary>
    /// Returns the capability list for the given agent, cached for 5 minutes.
    /// </summary>
    Task<IReadOnlyList<AgentCapability>> GetAgentCapabilitiesAsync(int agentId, CancellationToken ct = default);

    /// <summary>
    /// Returns the latest BTC/USD price quote, cached for 30 seconds.
    /// Supplements the existing database price cache with an in-memory layer.
    /// </summary>
    Task<PriceQuote?> GetBtcUsdPriceAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns aggregated system statistics, cached for 30 seconds.
    /// </summary>
    Task<SystemStats> GetSystemStatsAsync(CancellationToken ct = default);

    /// <summary>
    /// Removes all cache entries related to the specified agent
    /// (agent data, reputation, capabilities).
    /// </summary>
    void InvalidateAgent(int agentId);
}

/// <summary>
/// Aggregated system statistics snapshot.
/// </summary>
public class SystemStats
{
    public int TotalAgents { get; set; }
    public int ActiveAgents { get; set; }
    public int SuspendedAgents { get; set; }

    public int TotalTasks { get; set; }
    public int PendingTasks { get; set; }
    public int InProgressTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int FailedTasks { get; set; }

    public int PaymentCount { get; set; }
    public long TotalSats { get; set; }
    public double TotalUsd { get; set; }

    public int HeldEscrows { get; set; }
    public int SettledEscrows { get; set; }
    public int CancelledEscrows { get; set; }
    public long HeldAmountSats { get; set; }

    public int TotalVerifications { get; set; }
    public int PassedVerifications { get; set; }
    public int FailedVerifications { get; set; }
    public double VerificationPassRate { get; set; }

    public int OpenDisputes { get; set; }
    public int ResolvedDisputes { get; set; }

    public double BtcUsdPrice { get; set; }
    public string PriceLastUpdated { get; set; } = string.Empty;

    public string Uptime { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
}
